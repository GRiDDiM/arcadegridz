package com.griddim.arcadegridz

import android.app.Activity
import android.content.Intent
import android.content.pm.ActivityInfo
import android.content.res.Configuration
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.view.Gravity
import android.view.InputDevice
import android.view.KeyEvent
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import org.json.JSONObject

/**
 * ArcadeGridz for Android ("the ag app" on phones and tablets). Native launcher in the same layout, themes and
 * features as the Windows app; the web games run in an embedded WebView through the site's app bridge.
 * UI lives in Launcher.kt (grid / details / music bar) and Panels.kt (options, account, daily rewards, scores,
 * welcome, sign-in, updates).
 */
class MainActivity : Activity() {
    lateinit var settings: Settings
    lateinit var music: Music
    lateinit var host: GameHost
    lateinit var root: FrameLayout
    lateinit var backdrop: Backdrop
    lateinit var launcherHost: FrameLayout
    lateinit var gameLayer: FrameLayout
    lateinit var overlay: FrameLayout
    var gameCover: View? = null
    var gameCoverText: TextView? = null
    var auth: LoopbackAuth? = null

    // catalog + launcher state
    var all: List<GameEntry> = emptyList()
    val visible = ArrayList<GameEntry>()
    val categories = ArrayList<String>()
    var categoryIndex = 0
    var storyMode = false
    var albumMode = false
    var selected: GameEntry? = null
    var offline = false
    var catalogLoaded = false

    // account
    var token: String? = null
    var account: Account? = null

    // game
    var running: GameEntry? = null
    var gameStarted = false
    var gameTimeout: Runnable? = null

    // startup / web-profile flows that share the WebView
    var sessionCheck: ((Boolean) -> Unit)? = null
    var pendingDesktopToken: String? = null
    var desktopDone: ((Boolean) -> Unit)? = null
    var signInDone: (() -> Unit)? = null
    var signOutDone: (() -> Unit)? = null
    var inLauncher = false

    var update: UpdateInfo? = null
    var updateCheckedAt = 0L

    val panels = ArrayList<Pair<View, () -> Unit>>()
    val v = LauncherViews()
    var preview: GameHost? = null
    var previewRun: Runnable? = null
    var clockRun: Runnable? = null
    var bannerRun: Runnable? = null
    var banners: List<Banner> = emptyList()
    var bannerSeconds = 30
    var bannerIndex = 0
    var lastTileClick = 0L
    var smokePlay: String? = null

    val isTablet get() = resources.configuration.smallestScreenWidthDp >= 600
    val isLandscape get() = resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        settings = Settings(this)
        // CI smoke-test hooks (adb shell am start ... --es agSmoke guest --es agTheme acid --es agPlay hyperball)
        intent?.getStringExtra("agTheme")?.let { settings.theme = it }
        if (intent?.getStringExtra("agSmoke") == "guest") { settings.guest = true; settings.showTesting = true }
        smokePlay = intent?.getStringExtra("agPlay")
        Themes.current = Themes.get(settings.theme)
        Net.userAgent = "ArcadeGridz-Android/${BuildConfig.VERSION_NAME}"
        Images.init(this)
        Sfx.init(this, !settings.sfxOff)
        music = Music(settings)
        host = GameHost(this)
        host.quality = settings.quality
        host.onMessage = { type, msg -> onWebMessage(type, msg) }
        token = settings.token
        account = Account.parse(settings.accountJson)
        applyOrientation()

        root = FrameLayout(this)
        root.setBackgroundColor(Color.BLACK)
        backdrop = Backdrop(this)
        backdrop.animated = !settings.stillBackground
        backdrop.scanlines = !settings.hideScanlines
        root.addView(backdrop, flp(MATCH, MATCH))
        launcherHost = FrameLayout(this)
        launcherHost.visibility = View.GONE
        root.addView(launcherHost, flp(MATCH, MATCH))
        gameLayer = FrameLayout(this)
        gameLayer.setBackgroundColor(Color.BLACK)
        gameLayer.visibility = View.INVISIBLE
        root.addView(gameLayer, flp(MATCH, MATCH))
        overlay = FrameLayout(this)
        root.addView(overlay, flp(MATCH, MATCH))
        // the WebView lives in the game layer (hidden) so the session check can run behind the splash
        gameLayer.addView(host.view, flp(MATCH, MATCH))
        setContentView(root)
        immersive()
        music.onChange = { refreshMusic() }

        showSplash()
        startup()
    }

    // ------------------------------------------------------------------ startup

    private fun showSplash() {
        val logo = ImageView(this)
        logo.adjustViewBounds = true
        try { assets.open("logo.png").use { logo.setImageBitmap(android.graphics.BitmapFactory.decodeStream(it)) } } catch (_: Exception) { }
        logo.alpha = 0f
        logo.scaleX = 0.92f; logo.scaleY = 0.92f
        val w = (resources.displayMetrics.widthPixels * if (isTablet) 0.42f else 0.72f).toInt()
        overlay.addView(logo, flp(w, WRAP, Gravity.CENTER))
        logo.animate().alpha(1f).scaleX(1f).scaleY(1f).setDuration(700).start()
        logo.tag = "splash"
        Sfx.play("jingle")
    }

    private fun hideSplash(then: () -> Unit) {
        val s = overlay.findViewWithTag<View>("splash")
        if (s == null) { then(); return }
        s.animate().alpha(0f).setDuration(350).withEndAction { overlay.removeView(s); then() }.start()
    }

    private fun startup() {
        val t0 = System.currentTimeMillis()
        loadCatalog(quiet = false)
        checkUpdates(prompt = true)
        bg({ Leaderboards.load() }) { if (inLauncher) refreshScores() }
        val cached = token
        checkSession { signedIn ->
            val wait = (1600 - (System.currentTimeMillis() - t0)).coerceAtLeast(0)
            mainHandler.postDelayed({
                hideSplash {
                    when {
                        signedIn -> enterLauncher()
                        cached != null && offlineNow() -> enterLauncher() // offline: keep the last account, scores sync later
                        settings.guest -> enterLauncher()
                        else -> showWelcome()
                    }
                }
            }, wait)
        }
    }

    fun offlineNow(): Boolean {
        val cm = getSystemService(CONNECTIVITY_SERVICE) as android.net.ConnectivityManager
        return cm.activeNetwork == null
    }

    /** Loads ?mode=session-check in the (hidden) WebView; the page answers with "hello". */
    fun checkSession(done: (Boolean) -> Unit) {
        var answered = false
        val timeout = Runnable { if (!answered) { answered = true; sessionCheck = null; done(false) } }
        sessionCheck = { ok -> if (!answered) { answered = true; mainHandler.removeCallbacks(timeout); done(ok) } }
        mainHandler.postDelayed(timeout, 12000)
        host.navigate("$SITE/?host=winapp&mode=session-check")
    }

    fun loadCatalog(quiet: Boolean, done: (() -> Unit)? = null) {
        bg({ Catalog.load(this, token) }) { r ->
            if (r != null) {
                all = r.games
                offline = r.offline
                catalogLoaded = true
                if (inLauncher) rebuildCatalogViews()
            } else if (!quiet) toast("COULD NOT REACH ARCADEGRIDZ. CHECK YOUR CONNECTION.")
            done?.invoke()
        }
    }

    fun checkUpdates(prompt: Boolean, manual: Boolean = false, done: ((String) -> Unit)? = null) {
        updateCheckedAt = System.currentTimeMillis()
        bg({ Updates.check() }) { u ->
            val newer = u != null && Updates.newer(u.version, BuildConfig.VERSION_NAME)
            update = if (newer) u else null
            if (inLauncher) refreshHeader()
            done?.invoke(when {
                u == null && !manual -> ""
                u == null -> "COULD NOT CHECK FOR UPDATES."
                newer -> "VERSION ${u.version} IS READY."
                else -> "YOU HAVE THE LATEST VERSION."
            })
            if (prompt && newer && settings.skippedUpdate != u!!.version) {
                if (inLauncher) showUpdatePrompt() else pendingUpdatePrompt = true
            }
        }
    }
    var pendingUpdatePrompt = false

    fun enterLauncher() {
        if (inLauncher) return
        inLauncher = true
        buildLauncher()
        launcherHost.alpha = 0f
        launcherHost.visibility = View.VISIBLE
        launcherHost.animate().alpha(1f).setDuration(350).start()
        if (!settings.musicOff) music.load(autoplay = true) else music.load(autoplay = false)
        if (pendingUpdatePrompt) { pendingUpdatePrompt = false; showUpdatePrompt() }
        runDaily(force = false)
        startClock()
        smokePlay?.let { id ->
            smokePlay = null
            mainHandler.postDelayed({ all.firstOrNull { it.id == id }?.let { launch(it) } }, 6000)
        }
    }

    // ------------------------------------------------------------------ account

    fun setAccount(t: String?, a: Account?) {
        val changed = (t != token) || (a?.id != account?.id)
        token = t
        account = a
        settings.token = t
        settings.accountJson = a?.json()
        if (t != null) settings.guest = false
        if (inLauncher) refreshHeader()
        if (changed && inLauncher) loadCatalog(quiet = true) // the owner gets owner-only entries
    }

    fun runDaily(force: Boolean) {
        val t = token ?: return
        val id = account?.id ?: return
        bg({ Daily.visit(t, id, force) }) { st ->
            if (st != null && (force || st.newSparkz > 0)) showDaily(st)
        }
    }

    fun signOut() {
        signOutDone = {
            setAccount(null, null)
            settings.guest = true
            toast("SIGNED OUT")
        }
        host.navigate("$SITE/?host=winapp&mode=signout")
        mainHandler.postDelayed({ signOutDone?.let { it(); signOutDone = null } }, 10000)
    }

    /** Google sign-in in the system browser, then hand the session to this app's web profile. */
    fun googleSignIn(status: (String) -> Unit, done: (Boolean) -> Unit) {
        auth?.cancel()
        val a = LoopbackAuth(this)
        auth = a
        a.start(status) { result, error ->
            auth = null
            if (result == null) { status(if (error == "CANCELLED") "" else "GOOGLE SIGN-IN FAILED: ${error ?: ""}".uppercase()); done(false); return@start }
            status("SIGNING IN AS ${result.name.uppercase()}...")
            pendingDesktopToken = result.token
            desktopDone = { ok ->
                if (!ok) { status("SIGN-IN FAILED. TRY AGAIN.") }
                done(ok)
            }
            host.navigate("$SITE/?host=winapp&mode=desktop-complete")
            mainHandler.postDelayed({ desktopDone?.let { pendingDesktopToken = null; desktopDone = null; it(false) } }, 25000)
        }
    }

    // ------------------------------------------------------------------ web bridge

    private fun onWebMessage(type: String, m: JSONObject) {
        log("bridge", type)
        if (type == "hello" || type == "auth") {
            val tok = m.optString("token").takeIf { it.isNotBlank() && !m.isNull("token") }
            val acct = Account.parse(m.optJSONObject("account"))
            if (tok != null && acct != null) setAccount(tok, acct)
            else if (type == "hello" && tok == null && sessionCheck != null) {
                // no web session in this profile: forget the cached account
                if (!offlineNow()) setAccount(null, null)
            }
        }
        when (type) {
            "hello" -> {
                val ok = m.optBoolean("signedIn") && m.optJSONObject("account") != null
                sessionCheck?.let { sessionCheck = null; it(ok) }
            }
            "desktop-ready" -> pendingDesktopToken?.let { t ->
                pendingDesktopToken = null
                host.send(JSONObject().put("type", "desktop-auth").put("token", t))
            }
            "auth" -> {
                desktopDone?.let { desktopDone = null; it(true) }
                signInDone?.let { signInDone = null; it() }
                runDaily(force = false)
            }
            "signedout" -> {
                signOutDone?.let { signOutDone = null; it() }
            }
            "started" -> if (running != null) {
                gameStarted = true
                gameTimeout?.let { mainHandler.removeCallbacks(it) }
                gameCover?.animate()?.alpha(0f)?.setDuration(250)?.withEndAction { gameCover?.visibility = View.GONE }?.start()
                host.view.requestFocus()
            }
            "exited" -> if (running != null) closeGame()
            "error" -> {
                val err = m.optString("error")
                if (running != null) {
                    if (err == "app_page_lost" || !gameStarted) { toast("THE GAME COULD NOT START ($err)"); Sfx.play("error"); closeGame() }
                } else if (desktopDone != null) {
                    desktopDone?.let { desktopDone = null; it(false) }
                }
            }
        }
    }

    // ------------------------------------------------------------------ games

    fun launch(g: GameEntry, trailer: Boolean = false) {
        if (g.album != null) { music.selectAlbum(g.album); toast("NOW PLAYING: ${g.name}"); return }
        if (!g.playable || g.url == null) { Sfx.play("error"); toast("THIS GAME CAN'T LAUNCH YET"); return }
        stopPreview()
        running = g
        gameStarted = false
        Sfx.play("launch")
        music.hold(true)
        requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        if (Build.VERSION.SDK_INT >= 28) window.attributes = window.attributes.apply { layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES }
        showGameCover(g)
        gameLayer.visibility = View.VISIBLE
        launcherHost.visibility = View.INVISIBLE
        backdrop.visibility = View.INVISIBLE
        host.quality = settings.quality
        host.launch(g, trailer)
        host.view.requestFocus()
        immersive()
        gameTimeout = Runnable {
            if (running == g && !gameStarted) { toast("THE GAME TOOK TOO LONG TO START"); closeGame() }
        }.also { mainHandler.postDelayed(it, 30000) }
    }

    private fun showGameCover(g: GameEntry) {
        gameCover?.let { gameLayer.removeView(it) }
        val f = FrameLayout(this)
        f.setBackgroundColor(Themes.current.ink)
        val art = ImageView(this)
        art.scaleType = ImageView.ScaleType.CENTER_CROP
        art.alpha = 0.35f
        Images.load(g.art, art)
        f.addView(art, flp(MATCH, MATCH))
        val col = LinearLayout(this)
        col.orientation = LinearLayout.VERTICAL
        col.gravity = Gravity.CENTER
        col.addView(Ui.text(this, g.name.uppercase(), 30f, Color.WHITE, Font.DISPLAY, glow = true).apply { gravity = Gravity.CENTER })
        val status = Ui.text(this, "LOADING GAME...", 10f, Themes.current.cyan, Font.PIXEL, glow = true)
        status.gravity = Gravity.CENTER
        gameCoverText = status
        col.addView(status, lp(WRAP, WRAP).margins(t = dp(18)))
        col.addView(Ui.text(this, "◀  BACK OPENS THE SYSTEM MENU", 7f, Themes.current.dim, Font.PIXEL), lp(WRAP, WRAP).margins(t = dp(26)))
        f.addView(col, flp(MATCH, MATCH))
        f.alpha = 1f
        gameCover = f
        gameLayer.addView(f, flp(MATCH, MATCH))
        status.animate().alpha(0.35f).setDuration(600).withEndAction { status.animate().alpha(1f).setDuration(600).start() }.start()
    }

    fun closeGame() {
        val g = running ?: return
        running = null
        gameStarted = false
        gameTimeout?.let { mainHandler.removeCallbacks(it) }
        host.blank()
        gameCover?.let { gameLayer.removeView(it) }
        gameCover = null
        gameLayer.visibility = View.INVISIBLE
        launcherHost.visibility = View.VISIBLE
        backdrop.visibility = View.VISIBLE
        window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        applyOrientation()
        music.hold(false)
        Sfx.play("back")
        // scores the run may have posted
        bg({ Leaderboards.load() }) { refreshScores() }
        if (selected?.id != g.id) selectGame(g)
        focusPlay()
        startPreviewSoon()
    }

    fun applyOrientation() {
        if (running != null) return
        requestedOrientation = when (settings.orientation) {
            "landscape" -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            "portrait" -> ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT
            else -> ActivityInfo.SCREEN_ORIENTATION_FULL_USER
        }
    }

    // ------------------------------------------------------------------ input

    override fun dispatchKeyEvent(e: KeyEvent): Boolean {
        if (running != null) {
            // a pad's B falls back to BACK when the page doesn't consume it: that is in-game input, not "leave"
            if (e.keyCode == KeyEvent.KEYCODE_BACK && (e.flags and KeyEvent.FLAG_FALLBACK) != 0) return true
            return super.dispatchKeyEvent(e)
        }
        if (e.action == KeyEvent.ACTION_DOWN && e.repeatCount == 0) {
            when (e.keyCode) {
                KeyEvent.KEYCODE_BUTTON_L1 -> { if (panels.isEmpty()) { changeCategory(-1); return true } }
                KeyEvent.KEYCODE_BUTTON_R1 -> { if (panels.isEmpty()) { changeCategory(1); return true } }
                KeyEvent.KEYCODE_BUTTON_START -> { if (panels.isEmpty() && inLauncher) { showOptions(); return true } }
                KeyEvent.KEYCODE_BUTTON_Y -> { if (panels.isEmpty() && inLauncher) { toggleTesting(); return true } }
                KeyEvent.KEYCODE_BUTTON_X -> { if (panels.isEmpty() && inLauncher) { selected?.let { if (it.trailer) launch(it, trailer = true) }; return true } }
            }
        }
        return super.dispatchKeyEvent(e)
    }

    @Deprecated("Deprecated in Java")
    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        if (running != null) { host.systemMenu(); return }
        if (panels.isNotEmpty()) { closePanel(); return }
        if (storyMode || albumMode) { storyMode = false; albumMode = false; applyFilter(); Sfx.play("back"); return }
        if (!inLauncher) super.onBackPressed() else moveTaskToBack(true)
    }

    // ------------------------------------------------------------------ panels (modal stack over the launcher)

    fun pushPanel(v: View, onClose: () -> Unit = {}) {
        overlay.addView(v, flp(MATCH, MATCH))
        v.alpha = 0f
        v.animate().alpha(1f).setDuration(180).start()
        panels.add(v to onClose)
        Sfx.play("open")
        stopPreview()
    }

    fun closePanel() {
        val (v, onClose) = panels.removeAt(panels.size - 1)
        v.animate().alpha(0f).setDuration(150).withEndAction { overlay.removeView(v) }.start()
        onClose()
        Sfx.play("back")
        if (panels.isEmpty()) { restoreFocus(); startPreviewSoon() }
    }

    fun closeAllPanels() { while (panels.isNotEmpty()) closePanel() }

    /** Dimmed full-screen scrim with a centered card; returns the card's content column. */
    fun modal(maxWidthDp: Int, onClose: () -> Unit = {}, fullHeight: Boolean = false): Pair<FrameLayout, LinearLayout> {
        val t = Themes.current
        val scrim = FrameLayout(this)
        scrim.setBackgroundColor(Themes.withAlpha(if (t.light) 0xFF3A2A5C.toInt() else 0xFF000000.toInt(), 0xAA))
        scrim.isClickable = true
        val card = LinearLayout(this)
        card.orientation = LinearLayout.VERTICAL
        card.background = Ui.card(this, 26f, 0xF5, Themes.withAlpha(t.cyan, 0x88))
        card.setPadding(dp(20), dp(18), dp(20), dp(18))
        card.isClickable = true
        val w = minOf(resources.displayMetrics.widthPixels - dp(24), dp(maxWidthDp))
        val h = if (fullHeight) resources.displayMetrics.heightPixels - dp(if (isTablet) 70 else 32) else WRAP
        scrim.addView(card, flp(w, h, Gravity.CENTER))
        pushPanel(scrim, onClose)
        return scrim to card
    }

    // ------------------------------------------------------------------ misc

    fun toast(s: String) = Toast.makeText(this, s, Toast.LENGTH_SHORT).show()

    fun openBrowser(url: String) {
        try { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) } catch (_: Exception) { toast("NO BROWSER FOUND") }
    }

    fun immersive() {
        if (Build.VERSION.SDK_INT >= 30) {
            window.setDecorFitsSystemWindows(false)
            window.insetsController?.let {
                it.hide(WindowInsets.Type.systemBars())
                it.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            }
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = (View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or View.SYSTEM_UI_FLAG_FULLSCREEN or
                View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_STABLE or
                View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN)
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) immersive()
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        if (inLauncher) buildLauncher()
        immersive()
    }

    override fun onPause() {
        super.onPause()
        music.hold(true)
        if (running == null) stopPreview()
        host.view.onPause()
    }

    override fun onResume() {
        super.onResume()
        host.view.onResume()
        if (running == null) {
            music.hold(false)
            if (inLauncher) {
                startPreviewSoon()
                if (System.currentTimeMillis() - updateCheckedAt > 30 * 60 * 1000) checkUpdates(prompt = false)
                if (System.currentTimeMillis() - Leaderboards.loadedAt > 5 * 60 * 1000) bg({ Leaderboards.load() }) { refreshScores() }
                loadCatalog(quiet = true)
            }
        }
        immersive()
    }

    override fun onDestroy() {
        auth?.cancel()
        music.release()
        stopPreview()
        host.destroy()
        super.onDestroy()
    }

    fun isGamepad(e: KeyEvent) = (e.source and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD

    fun neonDot(color: Int): View = View(this).apply {
        background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(color) }
    }
}
