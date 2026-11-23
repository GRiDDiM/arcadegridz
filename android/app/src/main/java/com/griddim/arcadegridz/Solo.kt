package com.griddim.arcadegridz

import android.graphics.BitmapFactory
import android.graphics.Color
import android.view.Gravity
import android.view.View
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout

/**
 * Solo builds: one game packaged as its own app (product flavor with BuildConfig.SOLO_GAME set, e.g. the
 * "nhameo" flavor = Nhà Mèo Mobile). Same engine as the ag app (WebView game host, in-game BACK menu, session
 * check so scores post when the WebView profile is signed in), but no launcher: it boots straight into the game
 * and shows a small home card (PLAY / EXIT) when the game is closed or can't load.
 */
val isSolo: Boolean get() = BuildConfig.SOLO_GAME.isNotBlank()

fun MainActivity.soloEntry(): GameEntry =
    all.firstOrNull { it.id == BuildConfig.SOLO_GAME } ?: GameEntry(
        BuildConfig.SOLO_GAME, BuildConfig.SOLO_NAME, "", "", "alpha", "", "", "magenta", "",
        potato = true, touch = true, playable = true, trailer = false, released = "", multiplayer = "",
        runtimeType = "web", url = "$SITE/?host=winapp&play=${BuildConfig.SOLO_GAME}"
    )

fun MainActivity.showSoloSplash() {
    val col = LinearLayout(this)
    col.orientation = LinearLayout.VERTICAL
    col.gravity = Gravity.CENTER
    val icon = ImageView(this)
    try { icon.setImageBitmap(BitmapFactory.decodeResource(resources, R.mipmap.ic_launcher)) } catch (_: Exception) { }
    col.addView(icon, lp(dp(96), dp(96)))
    col.addView(Ui.text(this, BuildConfig.SOLO_NAME, 30f, Color.WHITE, Font.DISPLAY, glow = true).apply { gravity = Gravity.CENTER }, lp(WRAP, WRAP).margins(t = dp(14)))
    col.addView(Ui.text(this, "AN ARCADEGRIDZ GAME BY BDC", 7f, Themes.current.dim, Font.PIXEL), lp(WRAP, WRAP).margins(t = dp(10)))
    col.alpha = 0f
    col.tag = "splash"
    overlay.addView(col, flp(WRAP, WRAP, Gravity.CENTER))
    col.animate().alpha(1f).setDuration(600).start()
    Sfx.play("jingle")
}

/** Boot path for solo builds: session check (so scores can post), then straight into the game. */
fun MainActivity.soloStartup() {
    val t0 = System.currentTimeMillis()
    loadCatalog(quiet = true)
    checkSession { _ ->
        val wait = (1800 - (System.currentTimeMillis() - t0)).coerceAtLeast(0)
        mainHandler.postDelayed({ soloHideSplash { soloPlay() } }, wait)
    }
}

fun MainActivity.soloHideSplash(then: () -> Unit) {
    val s = overlay.findViewWithTag<View>("splash") ?: run { then(); return }
    s.animate().alpha(0f).setDuration(300).withEndAction { overlay.removeView(s); then() }.start()
}

fun MainActivity.soloPlay() {
    overlay.findViewWithTag<View>("solohome")?.let { overlay.removeView(it) }
    if (offlineNow()) { soloHome("NO CONNECTION — THE GAME NEEDS THE INTERNET"); return }
    launch(soloEntry())
}

/** Small home card over the backdrop: shown after the game closes (or fails to load). */
fun MainActivity.soloHome(note: String? = null) {
    overlay.findViewWithTag<View>("solohome")?.let { overlay.removeView(it) }
    overlay.visibility = View.VISIBLE
    backdrop.visibility = View.VISIBLE
    val card = LinearLayout(this)
    card.background = Ui.card(this)
    card.orientation = LinearLayout.VERTICAL
    card.gravity = Gravity.CENTER_HORIZONTAL
    card.setPadding(dp(28), dp(22), dp(28), dp(22))
    val icon = ImageView(this)
    try { icon.setImageBitmap(BitmapFactory.decodeResource(resources, R.mipmap.ic_launcher)) } catch (_: Exception) { }
    card.addView(icon, lp(dp(72), dp(72)))
    card.addView(Ui.text(this, BuildConfig.SOLO_NAME, 24f, Color.WHITE, Font.DISPLAY, glow = true).apply { gravity = Gravity.CENTER }, lp(WRAP, WRAP).margins(t = dp(10)))
    if (note != null) card.addView(Ui.text(this, note, 7f, Themes.current.magenta, Font.PIXEL).apply { gravity = Gravity.CENTER }, lp(WRAP, WRAP).margins(t = dp(10)))
    val play = Ui.button(this, "PLAY", "▶", Themes.current.cyan, 12f) { soloPlay() }
    card.addView(play, lp(dp(220), WRAP).margins(t = dp(18)))
    card.addView(Ui.button(this, "EXIT", "✕", Themes.current.magenta, 10f) { finishAndRemoveTask() }, lp(dp(220), WRAP).margins(t = dp(10)))
    val wrap = FrameLayout(this)
    wrap.tag = "solohome"
    wrap.addView(card, flp(WRAP, WRAP, Gravity.CENTER))
    overlay.addView(wrap, flp(MATCH, MATCH))
    play.requestFocus()
}
