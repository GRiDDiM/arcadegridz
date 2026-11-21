package com.griddim.arcadegridz

import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

// ===================================================================== shared bits

private fun MainActivity.panelHeader(card: LinearLayout, title: String, accent: Int = Themes.current.cyan, close: () -> Unit = { closePanel() }) {
    val row = LinearLayout(this)
    row.orientation = LinearLayout.HORIZONTAL
    row.gravity = Gravity.CENTER_VERTICAL
    row.addView(Ui.text(this, title, if (isTablet) 26f else 22f, accent, Font.DISPLAY, glow = true), lp(0, WRAP, 1f))
    row.addView(Ui.button(this, "", "✕", Themes.current.danger, 9f) { close() }, lp(WRAP, WRAP))
    card.addView(row, lp(MATCH, WRAP).margins(b = dp(10)))
}

/** Focusable ON/OFF row (pad friendly), like the Windows toggle switches. */
private fun MainActivity.toggleRow(label: String, get: () -> Boolean, set: (Boolean) -> Unit): View {
    val t = Themes.current
    val row = LinearLayout(this)
    row.orientation = LinearLayout.HORIZONTAL
    row.gravity = Gravity.CENTER_VERTICAL
    row.background = Ui.buttonBackground(this, t.cyan, 12f, Ui.rounded(Themes.withAlpha(t.card, 0x99), 12f, this))
    row.setPadding(dp(14), dp(12), dp(14), dp(12))
    row.isFocusable = true; row.isClickable = true
    row.addView(Ui.text(this, label, 8f, t.text, Font.PIXEL).apply { setLineSpacing(dp(4).toFloat(), 1f) }, lp(0, WRAP, 1f))
    val pill = TextView(this)
    pill.typeface = Themes.pixel(this)
    pill.setTextSize(TypedValue.COMPLEX_UNIT_SP, Themes.pixelSize(8f))
    pill.gravity = Gravity.CENTER
    pill.setPadding(dp(12), dp(7), dp(12), dp(7))
    fun paint() {
        val on = get()
        pill.text = if (on) "ON" else "OFF"
        pill.setTextColor(if (on) t.ink else t.muted)
        pill.background = if (on) Ui.rounded(t.cyan, 14f, this) else Ui.rounded(Color.TRANSPARENT, 14f, this, t.muted, 1.5f)
    }
    paint()
    row.addView(pill, lp(dp(62), WRAP).margins(l = dp(12)))
    row.setOnClickListener { set(!get()); paint(); Sfx.play("select") }
    return row
}

/** Focusable row that cycles through choices. */
private fun MainActivity.cycleRow(label: String, hint: String, choices: List<Pair<String, String>>, get: () -> String, set: (String) -> Unit): View {
    val t = Themes.current
    val row = LinearLayout(this)
    row.orientation = LinearLayout.HORIZONTAL
    row.gravity = Gravity.CENTER_VERTICAL
    row.background = Ui.buttonBackground(this, t.cyan, 12f, Ui.rounded(Themes.withAlpha(t.card, 0x99), 12f, this))
    row.setPadding(dp(14), dp(12), dp(14), dp(12))
    row.isFocusable = true; row.isClickable = true
    val left = LinearLayout(this)
    left.orientation = LinearLayout.VERTICAL
    left.addView(Ui.text(this, label, 8f, t.text, Font.PIXEL))
    if (hint.isNotEmpty()) left.addView(Ui.text(this, hint, 13f, t.muted, Font.BODY), lp(MATCH, WRAP).margins(t = dp(5)))
    row.addView(left, lp(0, WRAP, 1f))
    val value = Ui.text(this, "", 8f, t.cyan, Font.PIXEL)
    fun paint() { value.text = "◀  " + (choices.firstOrNull { it.first == get() }?.second ?: choices[0].second) + "  ▶" }
    paint()
    row.addView(value, lp(WRAP, WRAP).margins(l = dp(10)))
    row.setOnClickListener {
        val i = choices.indexOfFirst { it.first == get() }
        set(choices[(i + 1) % choices.size].first)
        paint()
        Sfx.play("move")
    }
    return row
}

private fun MainActivity.section(c: LinearLayout, v: View) = c.addView(v, lp(MATCH, WRAP).margins(b = dp(10)))

// ===================================================================== welcome / sign in

fun MainActivity.showWelcome(fromLauncher: Boolean = false) {
    val t = Themes.current
    val (_, card) = modal(500, onClose = { if (!inLauncher) finishAffinity() })
    card.gravity = Gravity.CENTER_HORIZONTAL
    val close = Ui.button(this, "", "✕", t.danger, 8f) { closePanel() }
    val top = FrameLayout(this)
    val coin = Ui.text(this, "INSERT COIN  ·  TAP TO START", 8f, t.gold, Font.PIXEL, glow = true)
    top.addView(coin, flp(WRAP, WRAP, Gravity.CENTER))
    top.addView(close, flp(WRAP, WRAP, Gravity.END or Gravity.CENTER_VERTICAL))
    card.addView(top, lp(MATCH, WRAP))
    blink(coin)
    card.addView(Ui.text(this, "ENTER THE GRID", 32f, t.cyan, Font.DISPLAY, glow = true).apply { gravity = Gravity.CENTER }, lp(WRAP, WRAP).margins(t = dp(10)))
    card.addView(Ui.text(this, "ONE GRiDDiM ACCOUNT FOR EVERY NETWORK SITE", 7f, t.muted, Font.PIXEL).apply { gravity = Gravity.CENTER }, lp(WRAP, WRAP).margins(t = dp(8), b = dp(16)))
    val status = Ui.text(this, "", 7.5f, t.gold, Font.PIXEL)
    status.gravity = Gravity.CENTER
    lateinit var google: NeonButton
    val email = Ui.button(this, "EMAIL / PASSWORD", "✉", t.cyan, 10f) { showEmailSignIn(fromLauncher) }
    card.addView(email, lp(MATCH, WRAP).margins(b = dp(10)))
    google = Ui.button(this, "CONTINUE WITH GOOGLE", "G", t.violet, 10f) {
        google.isEnabled = false
        googleSignIn({ s -> status.text = s }) { ok ->
            google.isEnabled = true
            if (ok) { closeAllPanelsSilently(); if (!inLauncher) enterLauncher() else toast("SIGNED IN") }
        }
    }
    card.addView(google, lp(MATCH, WRAP).margins(b = dp(10)))
    val guest = Ui.button(this, "PLAY AS GUEST", "▶", t.green, 10f) {
        settings.guest = true
        closeAllPanelsSilently()
        if (!inLauncher) enterLauncher()
    }
    card.addView(guest, lp(MATCH, WRAP))
    card.addView(status, lp(MATCH, WRAP).margins(t = dp(10)))
    card.addView(Ui.text(this, "By signing in you agree to the GRiDDiM Network Terms of Service and acknowledge the Privacy Policy.", 12f, t.dim, Font.BODY).apply { gravity = Gravity.CENTER }, lp(MATCH, WRAP).margins(t = dp(12)))
    val legal = LinearLayout(this)
    legal.gravity = Gravity.CENTER
    legal.addView(Ui.button(this, "TERMS OF SERVICE", null, t.dim, 7f) { openBrowser("https://griddim.com/terms/") }, lp(WRAP, WRAP).margins(r = dp(8)))
    legal.addView(Ui.button(this, "PRIVACY POLICY", null, t.dim, 7f) { openBrowser("https://griddim.com/privacy/") }, lp(WRAP, WRAP))
    card.addView(legal, lp(MATCH, WRAP).margins(t = dp(8)))
    if (!email.isInTouchMode) email.requestFocus()
}

/** Puts the shared WebView back in the (hidden) game layer after the sign-in card used it. */
fun MainActivity.returnHostView() {
    if (host.view.parent === gameLayer) return
    (host.view.parent as? ViewGroup)?.removeView(host.view)
    gameLayer.addView(host.view, 0, flp(MATCH, MATCH))
    host.blank()
}

/** Closes every panel without firing the startup "close = quit" handler. */
fun MainActivity.closeAllPanelsSilently() {
    while (panels.isNotEmpty()) {
        val (v, _) = panels.removeAt(panels.size - 1)
        overlay.removeView(v)
    }
    restoreFocus()
}

private fun MainActivity.blink(v: View) {
    v.animate().alpha(0.25f).setDuration(650).withEndAction {
        v.animate().alpha(1f).setDuration(650).withEndAction { if (v.isAttachedToWindow) blink(v) }.start()
    }.start()
}

/** EMAIL / PASSWORD: the Network's own sign-in page in a floating card (same as the Windows app). */
fun MainActivity.showEmailSignIn(fromLauncher: Boolean) {
    val t = Themes.current
    val (_, card) = modal(620, fullHeight = true, onClose = { signInDone = null; returnHostView() })
    panelHeader(card, "SIGN IN", t.cyan)
    val frame = FrameLayout(this)
    frame.background = Ui.rounded(Color.BLACK, 16f, this, Themes.withAlpha(t.cyan, 0x44), 1f)
    frame.roundClip(dp(16).toFloat())
    (host.view.parent as? ViewGroup)?.removeView(host.view)
    frame.addView(host.view, flp(MATCH, MATCH))
    card.addView(frame, lp(MATCH, 0, 1f))
    signInDone = {
        returnHostView()
        closeAllPanelsSilently()
        if (!inLauncher) enterLauncher() else toast("SIGNED IN AS ${account?.name?.uppercase() ?: ""}")
    }
    host.navigate("$SITE/?host=winapp&mode=signin")
    host.view.requestFocus()
}

// ===================================================================== updates

fun MainActivity.showUpdatePrompt() {
    val u = update ?: return
    val t = Themes.current
    val (_, card) = modal(480)
    panelHeader(card, "UPDATE READY", t.gold)
    card.addView(Ui.text(this, "VERSION ${u.version}  ·  YOU HAVE ${BuildConfig.VERSION_NAME}", 8f, t.cyan, Font.PIXEL), lp(MATCH, WRAP).margins(b = dp(10)))
    card.addView(Ui.text(this, "A newer ArcadeGridz is out. Tap UPDATE NOW to download it, then open the download and install it over this version. Your sign-in and settings stay put.", 15f, t.muted, Font.BODY), lp(MATCH, WRAP).margins(b = dp(16)))
    val row = LinearLayout(this)
    val now = Ui.button(this, "UPDATE NOW", "⬇", t.gold, 10f, filled = Ui.gradient(intArrayOf(t.gold, t.green), 14f, this), labelColor = t.ink) {
        openBrowser(u.apkUrl); closePanel()
    }
    row.addView(now, lp(0, WRAP, 1f).margins(r = dp(10)))
    row.addView(Ui.button(this, "NOT NOW", null, t.dim, 9f) { settings.skippedUpdate = u.version; closePanel() }, lp(0, WRAP, 1f))
    card.addView(row, lp(MATCH, WRAP))
    if (!now.isInTouchMode) now.requestFocus()
}

// ===================================================================== daily rewards (SIGNAL VAULT)

fun MainActivity.showDaily(st: DailyState) {
    val t = Themes.current
    val (_, card) = modal(540)
    val scroll = ScrollView(this)
    val c = LinearLayout(this)
    c.orientation = LinearLayout.VERTICAL
    c.gravity = Gravity.CENTER_HORIZONTAL
    scroll.addView(c)
    card.addView(scroll, lp(MATCH, WRAP))
    val top = FrameLayout(this)
    top.addView(Ui.text(this, "GRiDDiM NETWORK  ·  DAILY REWARDS", 7f, t.violet, Font.PIXEL), flp(WRAP, WRAP, Gravity.CENTER_VERTICAL))
    top.addView(Ui.button(this, "", "✕", t.danger, 8f) { closePanel() }, flp(WRAP, WRAP, Gravity.END))
    c.addView(top, lp(MATCH, WRAP))
    c.addView(Ui.text(this, "SIGNAL VAULT", 34f, t.gold, Font.DISPLAY, glow = true), lp(WRAP, WRAP).margins(t = dp(6)))
    c.addView(Ui.text(this, "Seven days. Seven seals. Restore the signal.", 14f, t.muted, Font.BODY), lp(WRAP, WRAP).margins(t = dp(4), b = dp(14)))
    // seals
    val seals = LinearLayout(this)
    seals.gravity = Gravity.CENTER
    for (i in 1..7) {
        val f = FrameLayout(this)
        val done = i <= st.stage
        val today = i == st.stage
        val color = if (i == 7) t.magenta else t.gold
        f.background = GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setColor(if (done) Themes.withAlpha(color, if (today) 0xFF else 0x99) else Themes.withAlpha(t.card, 0xFF))
            setStroke(dp(2), if (done) color else Themes.withAlpha(t.muted, 0x66))
        }
        val n = Ui.text(this, if (done) "✓" else "$i", 9f, if (done) t.ink else t.muted, Font.PIXEL)
        n.gravity = Gravity.CENTER
        f.addView(n, flp(MATCH, MATCH))
        if (today) { f.scaleX = 1.18f; f.scaleY = 1.18f }
        seals.addView(f, lp(dp(if (isTablet) 46 else 36), dp(if (isTablet) 46 else 36)).margins(l = dp(4), r = dp(4)))
    }
    c.addView(seals, lp(MATCH, WRAP).margins(b = dp(16)))
    val drop = Ui.text(this, "+${"%,d".format(st.newSparkz)} SPARKZ", 30f, t.green, Font.DISPLAY, glow = true)
    c.addView(drop)
    drop.scaleX = 0.6f; drop.scaleY = 0.6f
    drop.animate().scaleX(1f).scaleY(1f).setDuration(500).start()
    c.addView(Ui.text(this, if (st.newSparkz > 0) st.rewards.filter { it.amount > 0 }.joinToString("  ·  ") { it.name.uppercase() } else "ALREADY COLLECTED TODAY", 7f, t.dim, Font.PIXEL).apply { gravity = Gravity.CENTER }, lp(MATCH, WRAP).margins(t = dp(6), b = dp(14)))
    val stats = LinearLayout(this)
    fun stat(label: String, value: String, color: Int) {
        val col = LinearLayout(this)
        col.orientation = LinearLayout.VERTICAL
        col.gravity = Gravity.CENTER
        col.addView(Ui.text(this, label, 6.5f, t.dim, Font.PIXEL))
        col.addView(Ui.text(this, value, 18f, color, Font.DISPLAY), lp(WRAP, WRAP).margins(t = dp(5)))
        stats.addView(col, lp(0, WRAP, 1f))
    }
    stat("YOUR SPARKZ", st.balance, t.cyan)
    stat("STREAK", "${st.streak} DAYS", t.gold)
    stat("DAY 7 BONUS", "+${"%,d".format(st.streakBonus)}", t.magenta)
    c.addView(stats, lp(MATCH, WRAP).margins(b = dp(14)))
    if (st.quests.isNotEmpty()) {
        c.addView(Ui.text(this, "DAILY SIGNALS", 8f, t.violet, Font.PIXEL), lp(MATCH, WRAP).margins(b = dp(6)))
        for (q in st.quests) {
            val row = LinearLayout(this)
            row.gravity = Gravity.CENTER_VERTICAL
            row.addView(Ui.text(this, if (q.completed) "✓" else "○", 15f, if (q.completed) t.green else t.dim, Font.BODY_BOLD), lp(dp(26), WRAP))
            val col = LinearLayout(this)
            col.orientation = LinearLayout.VERTICAL
            col.addView(Ui.text(this, q.name, 14f, t.text, Font.BODY_BOLD))
            if (q.description.isNotBlank()) col.addView(Ui.text(this, q.description, 12f, t.muted, Font.BODY))
            row.addView(col, lp(0, WRAP, 1f))
            row.addView(Ui.text(this, "+${q.reward}", 8f, t.gold, Font.PIXEL))
            c.addView(row, lp(MATCH, WRAP).margins(b = dp(8)))
        }
    }
    c.addView(Ui.text(this, "RESETS AT MIDNIGHT PACIFIC  ·  A MISSED DAY RESTARTS THE SEALS", 6f, t.dim, Font.PIXEL).apply { gravity = Gravity.CENTER }, lp(MATCH, WRAP).margins(t = dp(6), b = dp(12)))
    val collect = Ui.button(this, "COLLECT", "✦", t.gold, 11f, filled = Ui.gradient(intArrayOf(t.gold, t.magenta), 14f, this), labelColor = t.ink) { closePanel() }
    c.addView(collect, lp(MATCH, WRAP))
    if (!collect.isInTouchMode) collect.requestFocus()
    if (st.newSparkz > 0) Sfx.play("jingle")
}

// ===================================================================== account

fun MainActivity.showAccount() {
    val a = account ?: return
    val t = Themes.current
    val (_, card) = modal(460)
    panelHeader(card, "ACCOUNT", t.cyan)
    val row = LinearLayout(this)
    row.gravity = Gravity.CENTER_VERTICAL
    val av = FrameLayout(this)
    av.background = GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, intArrayOf(t.cyan, t.violet, t.magenta)).apply { shape = GradientDrawable.OVAL }
    val init = Ui.text(this, a.initial, 30f, t.ink, Font.DISPLAY)
    init.gravity = Gravity.CENTER
    av.addView(init, flp(MATCH, MATCH))
    val img = ImageView(this)
    img.scaleType = ImageView.ScaleType.CENTER_CROP
    av.addView(img, flp(MATCH, MATCH))
    av.roundClip(dp(36).toFloat())
    if (a.avatar.isNotBlank()) Images.load(a.avatar, img, 300)
    row.addView(av, lp(dp(72), dp(72)).margins(r = dp(16)))
    val col = LinearLayout(this)
    col.orientation = LinearLayout.VERTICAL
    col.addView(Ui.oneLine(Ui.text(this, a.name, 24f, t.text, Font.DISPLAY)))
    if (a.username.isNotBlank()) col.addView(Ui.text(this, "@${a.username}", 14f, t.muted, Font.BODY), lp(WRAP, WRAP).margins(t = dp(4)))
    row.addView(col, lp(0, WRAP, 1f))
    card.addView(row, lp(MATCH, WRAP))
    card.addView(Ui.text(this, "SIGNED IN TO THE GRiDDiM NETWORK  ·  SCORES POST TO YOUR PROFILE", 6.5f, t.violet, Font.PIXEL).apply { setLineSpacing(dp(4).toFloat(), 1f) }, lp(MATCH, WRAP).margins(t = dp(14), b = dp(16)))
    val daily = Ui.button(this, "DAILY REWARDS", "✦", t.gold, 10f) {
        val tok = token ?: return@button
        bg({ Daily.state(tok) }) { st -> if (st != null) { closePanel(); showDaily(st) } else toast("DAILY REWARDS ARE UNAVAILABLE RIGHT NOW") }
    }
    card.addView(daily, lp(MATCH, WRAP).margins(b = dp(10)))
    card.addView(Ui.button(this, "SIGN OUT", "⏻", t.danger, 10f) { closePanel(); signOut() }, lp(MATCH, WRAP))
    if (!daily.isInTouchMode) daily.requestFocus()
}

// ===================================================================== options

fun MainActivity.showOptions(tab: String = "games") {
    val t = Themes.current
    val (_, card) = modal(760, fullHeight = true)
    panelHeader(card, "OPTIONS", t.violet)
    val tabsScroll = HorizontalScrollView(this)
    tabsScroll.isHorizontalScrollBarEnabled = false
    val tabs = LinearLayout(this)
    tabsScroll.addView(tabs)
    card.addView(tabsScroll, lp(MATCH, WRAP).margins(b = dp(12)))
    val scroll = ScrollView(this)
    scroll.isVerticalScrollBarEnabled = false
    val content = LinearLayout(this)
    content.orientation = LinearLayout.VERTICAL
    scroll.addView(content)
    card.addView(scroll, lp(MATCH, 0, 1f))
    val names = listOf("games" to "GAMES", "display" to "DISPLAY", "themes" to "THEMES", "system" to "SYSTEM", "updates" to "UPDATES", "about" to "ABOUT")
    val buttons = HashMap<String, NeonButton>()
    fun open(id: String) {
        buttons.forEach { (k, b) -> b.isSelected = k == id }
        content.removeAllViews()
        when (id) {
            "games" -> optionsGames(content)
            "display" -> optionsDisplay(content)
            "themes" -> optionsThemes(content)
            "system" -> optionsSystem(content)
            "updates" -> optionsUpdates(content)
            else -> optionsAbout(content)
        }
        scroll.scrollTo(0, 0)
    }
    for ((id, label) in names) {
        val b = Ui.button(this, label, null, t.violet, 8f) { open(id) }
        buttons[id] = b
        tabs.addView(b, lp(WRAP, WRAP).margins(r = dp(8)))
    }
    open(tab)
    buttons[tab]?.let { if (!it.isInTouchMode) it.requestFocus() }
}

private fun MainActivity.optionsGames(c: LinearLayout) {
    val t = Themes.current
    section(c, toggleRow("SHOW GAMES THAT AREN'T TOUCH-READY YET (THEY MAY NEED A CONTROLLER)", { settings.showAll }) { settings.showAll = it; rebuildCatalogViews() })
    section(c, toggleRow("SHOW BETAS & ALPHAS (GAMES STILL IN TESTING)", { settings.showTesting }) { settings.showTesting = it; rebuildCatalogViews() })
    section(c, toggleRow("AUTOPLAY TRAILERS IN THE DETAILS PANEL (MUTED)", { !settings.noTrailerPreview }) { settings.noTrailerPreview = !it })
    val status = Ui.text(this, "", 7f, t.dim, Font.PIXEL)
    section(c, Ui.button(this, "REFRESH GAME LIST", "↻", t.cyan, 9f) {
        status.text = "SYNCING..."
        loadCatalog(quiet = false) { status.text = if (offline) "OFFLINE: SHOWING THE SAVED LIST" else "UP TO DATE  ·  ${all.size} ENTRIES" }
        bg({ Leaderboards.load() }) { refreshScores() }
    })
    section(c, status)
}

private fun MainActivity.optionsDisplay(c: LinearLayout) {
    section(c, cycleRow("LAUNCHER ORIENTATION", "Games always play sideways.", listOf("auto" to "AUTO", "landscape" to "LANDSCAPE", "portrait" to "PORTRAIT"), { settings.orientation }) {
        settings.orientation = it; applyOrientation()
    })
    section(c, cycleRow("GRAPHICS QUALITY", "HD remakes of some games are on the way.", listOf("standard" to "STANDARD"), { settings.quality }) { settings.quality = it })
    section(c, toggleRow("CRT SCANLINES ON MENUS", { !settings.hideScanlines }) { settings.hideScanlines = !it; backdrop.scanlines = it })
    section(c, toggleRow("ANIMATED BACKGROUND", { !settings.stillBackground }) { settings.stillBackground = !it; backdrop.animated = it })
}

private fun MainActivity.optionsThemes(c: LinearLayout) {
    val cols = if (isTablet) 3 else 2
    var row: LinearLayout? = null
    for ((i, th) in Themes.all.withIndex()) {
        if (i % cols == 0) { row = LinearLayout(this); c.addView(row, lp(MATCH, WRAP).margins(b = dp(10))) }
        row?.addView(themeCard(th), lp(0, WRAP, 1f).margins(r = if (i % cols < cols - 1) dp(10) else 0))
    }
    val rem = Themes.all.size % cols
    if (rem != 0) repeat(cols - rem) { row?.addView(View(this), lp(0, 1, 1f)) }
    c.addView(Ui.text(this, "ACTIVE: ${Themes.current.name}", 7f, Themes.current.dim, Font.PIXEL), lp(MATCH, WRAP).margins(t = dp(4)))
}

/** Preview card drawn in the theme's own colours and fonts. */
private fun MainActivity.themeCard(th: AgTheme): View {
    val active = th.id == Themes.current.id
    val card = LinearLayout(this)
    card.orientation = LinearLayout.VERTICAL
    card.setPadding(dp(14), dp(14), dp(14), dp(14))
    val normal = Ui.rounded(th.ink, 16f, this, if (active) th.cyan else Themes.withAlpha(th.cyan, 0x55), if (active) 3f else 1.5f)
    val focus = Ui.rounded(Themes.withAlpha(th.cyan, 0x40), 16f, this, th.cyan, 3f)
    card.background = android.graphics.drawable.StateListDrawable().apply {
        addState(intArrayOf(android.R.attr.state_pressed), focus)
        addState(intArrayOf(android.R.attr.state_focused), focus)
        addState(intArrayOf(), normal)
    }
    card.isFocusable = true; card.isClickable = true
    val font = th.displayFont?.let { try { Typeface.createFromAsset(assets, it) } catch (_: Exception) { null } } ?: Typeface.DEFAULT_BOLD
    val title = TextView(this)
    title.text = "${th.emoji}  ${th.name}"
    title.typeface = font
    title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 17f)
    title.setTextColor(th.cyan)
    title.maxLines = 1
    card.addView(title)
    val tag = TextView(this)
    tag.text = th.tagline
    tag.setTextColor(th.muted)
    tag.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
    card.addView(tag, lp(MATCH, WRAP).margins(t = dp(6), b = dp(10)))
    val sw = LinearLayout(this)
    for (col in th.swatches) {
        val d = View(this)
        d.background = Ui.rounded(col, 5f, this, Themes.withAlpha(th.text, 0x44), 1f)
        sw.addView(d, lp(0, dp(16), 1f).margins(r = dp(4)))
    }
    card.addView(sw, lp(MATCH, WRAP))
    if (active) card.addView(TextView(this).apply { text = "✓ ACTIVE"; setTextColor(th.green); typeface = Typeface.DEFAULT_BOLD; setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f) }, lp(WRAP, WRAP).margins(t = dp(8)))
    card.setOnClickListener {
        if (th.id == Themes.current.id) return@setOnClickListener
        Sfx.play("select")
        settings.theme = th.id
        Themes.current = th
        closeAllPanelsSilently()
        backdrop.invalidate()
        buildLauncher()
        showOptions("themes")
    }
    return card
}

private fun MainActivity.optionsSystem(c: LinearLayout) {
    section(c, toggleRow("PLAY THE GAME OVER ALBUM WHEN ARCADEGRIDZ STARTS", { !settings.musicOff }) { settings.musicOff = !it })
    section(c, toggleRow("MENU SOUNDS", { !settings.sfxOff }) { settings.sfxOff = !it; Sfx.enabled = it })
}

private fun MainActivity.optionsUpdates(c: LinearLayout) {
    val t = Themes.current
    section(c, Ui.text(this, "ARCADEGRIDZ FOR ANDROID  ·  V${BuildConfig.VERSION_NAME}", 9f, t.cyan, Font.PIXEL))
    val status = Ui.text(this, update?.let { "VERSION ${it.version} IS READY." } ?: "", 7.5f, t.gold, Font.PIXEL)
    section(c, status)
    val install = Ui.button(this, "INSTALL UPDATE", "⬇", t.gold, 9f) { update?.let { openBrowser(it.apkUrl) } }
    install.visibility = if (update != null) View.VISIBLE else View.GONE
    section(c, Ui.button(this, "CHECK FOR UPDATES", "↻", t.cyan, 9f) {
        status.text = "CHECKING..."
        checkUpdates(prompt = false, manual = true) { s -> status.text = s; install.visibility = if (update != null) View.VISIBLE else View.GONE }
    })
    section(c, install)
    section(c, Ui.text(this, "Updates download from the ArcadeGridz GitHub releases. Open the downloaded file to install it over this version.", 13f, t.muted, Font.BODY))
    section(c, Ui.button(this, "ALL RELEASES", "↗", t.violet, 8f) { openBrowser("https://github.com/GRiDDiM/arcadegridz/releases") })
}

private fun MainActivity.optionsAbout(c: LinearLayout) {
    val t = Themes.current
    section(c, Ui.text(this, "ARCADEGRIDZ  ·  V${BuildConfig.VERSION_NAME}", 9f, t.cyan, Font.PIXEL))
    section(c, Ui.text(this, "ArcadeGridz for Android is part of the GRiDDiM Network. Games and the game list stream from arcadegridz.com, so new games show up without an app update. Games by BDC.", 15f, t.muted, Font.BODY))
    val row = LinearLayout(this)
    row.addView(Ui.button(this, "TERMS OF SERVICE", null, t.dim, 7.5f) { openBrowser("https://griddim.com/terms/") }, lp(WRAP, WRAP).margins(r = dp(8)))
    row.addView(Ui.button(this, "PRIVACY POLICY", null, t.dim, 7.5f) { openBrowser("https://griddim.com/privacy/") }, lp(WRAP, WRAP))
    section(c, row)
}

// ===================================================================== hi-scores

fun MainActivity.showScores() {
    val t = Themes.current
    val sheet = FrameLayout(this)
    sheet.setBackgroundColor(Themes.withAlpha(t.ink, 0xF2))
    sheet.isClickable = true
    val col = LinearLayout(this)
    col.orientation = LinearLayout.VERTICAL
    col.setPadding(dp(if (isTablet) 28 else 14), dp(16), dp(if (isTablet) 28 else 14), dp(12))
    sheet.addView(col, flp(MATCH, MATCH))
    val head = LinearLayout(this)
    head.gravity = Gravity.CENTER_VERTICAL
    head.addView(Ui.button(this, "BACK", "◀", t.cyan, 8f) { closePanel() }, lp(WRAP, WRAP).margins(r = dp(14)))
    val titles = LinearLayout(this)
    titles.orientation = LinearLayout.VERTICAL
    titles.addView(Ui.text(this, "♛ HI-SCORES", if (isTablet) 28f else 22f, t.gold, Font.DISPLAY, glow = true))
    val summary = Ui.oneLine(Ui.text(this, "LOADING THE NETWORK BOARDS...", 6.5f, t.dim, Font.PIXEL))
    titles.addView(summary, lp(MATCH, WRAP).margins(t = dp(4)))
    head.addView(titles, lp(0, WRAP, 1f))
    col.addView(head, lp(MATCH, WRAP).margins(b = dp(12)))

    val games = ArrayList<GameEntry>()
    val board = LinearLayout(this)
    board.orientation = LinearLayout.VERTICAL
    val body = LinearLayout(this)
    body.orientation = if (isTablet && isLandscape) LinearLayout.HORIZONTAL else LinearLayout.VERTICAL
    col.addView(body, lp(MATCH, 0, 1f))

    var current = 0
    val listCol = LinearLayout(this)
    listCol.orientation = LinearLayout.VERTICAL
    val pickerName = Ui.oneLine(Ui.text(this, "", 18f, t.text, Font.DISPLAY))

    fun showBoard(i: Int) {
        if (games.isEmpty()) return
        current = (i + games.size) % games.size
        val g = games[current]
        pickerName.text = g.name
        for (k in 0 until listCol.childCount) listCol.getChildAt(k).isSelected = k == current
        board.removeAllViews()
        val banner = FrameLayout(this)
        banner.roundClip(dp(14).toFloat())
        val art = ImageView(this)
        art.scaleType = ImageView.ScaleType.CENTER_CROP
        Images.load(g.art, art, 900)
        banner.addView(art, flp(MATCH, MATCH))
        val scrim = View(this)
        scrim.background = GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, intArrayOf(0xE6050312.toInt(), 0x66050312))
        banner.addView(scrim, flp(MATCH, MATCH))
        val bt = LinearLayout(this)
        bt.orientation = LinearLayout.VERTICAL
        bt.setPadding(dp(16), dp(12), dp(16), dp(12))
        bt.addView(Ui.text(this, g.genre.uppercase(), 7f, t.cyan, Font.PIXEL))
        bt.addView(Ui.oneLine(Ui.text(this, g.name, 24f, Color.WHITE, Font.DISPLAY)), lp(MATCH, WRAP).margins(t = dp(4)))
        banner.addView(bt, flp(MATCH, WRAP, Gravity.CENTER_VERTICAL))
        board.addView(banner, lp(MATCH, dp(92)).margins(b = dp(10)))
        val hdr = LinearLayout(this)
        hdr.setPadding(dp(4), 0, dp(4), dp(4))
        hdr.addView(Ui.text(this, "RANK", 6.5f, t.dim, Font.PIXEL), lp(dp(42), WRAP))
        hdr.addView(Ui.text(this, "PLAYER", 6.5f, t.dim, Font.PIXEL), lp(0, WRAP, 1f))
        hdr.addView(Ui.text(this, "SCORE", 6.5f, t.dim, Font.PIXEL), lp(WRAP, WRAP).margins(r = dp(8)))
        hdr.addView(Ui.text(this, "STATUS", 6.5f, t.dim, Font.PIXEL))
        board.addView(hdr, lp(MATCH, WRAP))
        val scores = Leaderboards.all[g.id].orEmpty()
        val me = account?.name
        if (scores.isEmpty()) {
            board.addView(Ui.text(this, "NO SCORES YET", 12f, t.muted, Font.PIXEL), lp(WRAP, WRAP).margins(t = dp(18)))
            board.addView(Ui.text(this, "PLAY IT AND TAKE THE #1 SPOT.", 7f, t.dim, Font.PIXEL), lp(WRAP, WRAP).margins(t = dp(8)))
        }
        for ((k, s) in scores.withIndex()) board.addView(scoreRow("#${k + 1}", s.name, "%,d".format(s.score), s.tag, me != null && s.name.equals(me, true)))
        val mine = if (me == null) null else scores.indexOfFirst { it.name.equals(me, true) }.takeIf { it >= 0 }
        val foot = LinearLayout(this)
        foot.gravity = Gravity.CENTER_VERTICAL
        foot.addView(Ui.text(this, if (mine != null) "YOUR BEST: #${mine + 1}  ·  ${"%,d".format(scores[mine].score)}" else if (me != null) "YOU HAVEN'T POSTED A SCORE HERE YET" else "SIGN IN TO POST SCORES", 7f, t.gold, Font.PIXEL), lp(0, WRAP, 1f))
        if (g.playable && g.url != null) foot.addView(Ui.button(this, "PLAY IT", "▶", t.green, 9f) { closePanel(); launch(g) })
        board.addView(foot, lp(MATCH, WRAP).margins(t = dp(14)))
    }

    if (isTablet && isLandscape) {
        val ls = ScrollView(this)
        ls.isVerticalScrollBarEnabled = false
        ls.addView(listCol)
        body.addView(ls, lp(0, MATCH, 0.42f).margins(r = dp(18)))
    } else {
        val picker = LinearLayout(this)
        picker.gravity = Gravity.CENTER_VERTICAL
        picker.addView(Ui.button(this, "", "◀", t.cyan, 9f) { showBoard(current - 1) })
        pickerName.gravity = Gravity.CENTER
        picker.addView(pickerName, lp(0, WRAP, 1f).margins(l = dp(10), r = dp(10)))
        picker.addView(Ui.button(this, "", "▶", t.cyan, 9f) { showBoard(current + 1) })
        body.addView(picker, lp(MATCH, WRAP).margins(b = dp(10)))
    }
    val bs = ScrollView(this)
    bs.isVerticalScrollBarEnabled = false
    bs.addView(board)
    body.addView(bs, if (isTablet && isLandscape) lp(0, MATCH, 0.58f) else lp(MATCH, 0, 1f))

    fun fill() {
        games.clear()
        val boards = Leaderboards.all
        games.addAll(all.filter { !it.isStoryboard && (listed(it) || boards[it.id].orEmpty().isNotEmpty()) }
            .sortedWith(compareByDescending<GameEntry> { boards[it.id].orEmpty().isNotEmpty() }.thenBy { it.name.lowercase() }))
        val total = games.sumOf { boards[it.id].orEmpty().size }
        summary.text = "${games.count { boards[it.id].orEmpty().isNotEmpty() }} GAMES WITH SCORES  ·  $total RUNS ON THE NETWORK BOARDS"
        listCol.removeAllViews()
        for ((i, g) in games.withIndex()) {
            val row = LinearLayout(this)
            row.gravity = Gravity.CENTER_VERTICAL
            row.setPadding(dp(8), dp(8), dp(10), dp(8))
            row.background = Ui.buttonBackground(this, t.gold, 12f, Ui.rounded(Color.TRANSPARENT, 12f, this))
            row.isFocusable = true; row.isClickable = true
            val art = ImageView(this)
            art.scaleType = ImageView.ScaleType.CENTER_CROP
            art.roundClip(dp(8).toFloat())
            Images.load(g.art, art, 200)
            row.addView(art, lp(dp(46), dp(46)).margins(r = dp(12)))
            val c2 = LinearLayout(this)
            c2.orientation = LinearLayout.VERTICAL
            c2.addView(Ui.oneLine(Ui.text(this, g.name, 15f, t.text, Font.DISPLAY)))
            val s = boards[g.id].orEmpty()
            c2.addView(Ui.oneLine(Ui.text(this, if (s.isEmpty()) "NO SCORES YET" else "TOP ${"%,d".format(s[0].score)}  ·  ${s.size} RUNS", 6.5f, if (s.isEmpty()) t.dim else t.gold, Font.PIXEL)), lp(MATCH, WRAP).margins(t = dp(4)))
            row.alpha = if (s.isEmpty()) 0.6f else 1f
            row.addView(c2, lp(0, WRAP, 1f))
            row.setOnClickListener { Sfx.play("move"); showBoard(i) }
            listCol.addView(row, lp(MATCH, WRAP).margins(b = dp(6)))
        }
        val start = selected?.let { sel -> games.indexOfFirst { it.id == sel.id } }?.takeIf { it >= 0 } ?: 0
        showBoard(start)
        if (listCol.childCount > 0 && !listCol.isInTouchMode) listCol.getChildAt(start).requestFocus()
    }

    pushPanel(sheet)
    fill()
    bg({ Leaderboards.load() }) { if (sheet.isAttachedToWindow) fill() }
}
