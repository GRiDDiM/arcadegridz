package com.griddim.arcadegridz

import android.graphics.Color
import android.view.Gravity
import android.view.View
import android.widget.FrameLayout
import android.widget.LinearLayout

/**
 * Native in-game menu (Android BACK). Always reachable, even when the page shows nothing:
 * RESUME / GAME MENU (the site's system menu) / RELOAD / EXIT GAME.
 */
fun MainActivity.toggleGameMenu() { if (gameMenu != null) hideGameMenu() else showGameMenu(null) }

fun MainActivity.showGameMenu(note: String?) {
    val g = running ?: return
    hideGameMenu()
    val t = Themes.current
    val scrim = FrameLayout(this)
    scrim.setBackgroundColor(0xB3000000.toInt())
    scrim.isClickable = true
    val card = LinearLayout(this)
    card.orientation = LinearLayout.VERTICAL
    card.gravity = Gravity.CENTER_HORIZONTAL
    card.background = Ui.card(this, 24f, 0xF5, Themes.withAlpha(t.cyan, 0x99))
    card.setPadding(dp(22), dp(18), dp(22), dp(18))
    card.addView(Ui.text(this, "PAUSED", 9f, t.gold, Font.PIXEL, glow = true))
    card.addView(Ui.oneLine(Ui.text(this, g.name, 24f, if (t.light) t.text else Color.WHITE, Font.DISPLAY, glow = true)), lp(WRAP, WRAP).margins(t = dp(6), b = dp(if (note != null) 6 else 14)))
    if (note != null) card.addView(Ui.text(this, note, 7f, t.gold, Font.PIXEL).apply { gravity = Gravity.CENTER }, lp(WRAP, WRAP).margins(b = dp(14)))
    val row = LinearLayout(this)
    row.gravity = Gravity.CENTER
    val resume = Ui.button(this, "RESUME", "▶", t.green, 9f) { hideGameMenu() }
    row.addView(resume, lp(WRAP, WRAP).margins(r = dp(10)))
    if (gameStarted) row.addView(Ui.button(this, "GAME MENU", "≡", t.cyan, 9f) { hideGameMenu(); host.systemMenu() }, lp(WRAP, WRAP).margins(r = dp(10)))
    row.addView(Ui.button(this, "RELOAD", "↻", t.violet, 9f) { hideGameMenu(); val cur = g; closeGameNow(); launch(cur) }, lp(WRAP, WRAP).margins(r = dp(10)))
    row.addView(Ui.button(this, "EXIT GAME", "✕", t.danger, 9f) { hideGameMenu(); closeGameNow() }, lp(WRAP, WRAP))
    card.addView(row, lp(WRAP, WRAP))
    scrim.addView(card, flp(WRAP, WRAP, Gravity.CENTER))
    gameLayer.addView(scrim, flp(MATCH, MATCH))
    gameMenu = scrim
    Sfx.play("open")
    if (!resume.isInTouchMode) resume.requestFocus()
}

fun MainActivity.hideGameMenu() {
    val m = gameMenu ?: return
    gameMenu = null
    gameLayer.removeView(m)
    if (running != null) host.view.requestFocus()
}

/** EXIT that never depends on the page answering: ask it to leave, then close the game layer right away. */
fun MainActivity.closeGameNow() {
    if (running == null) return
    try { host.exit() } catch (_: Exception) { }
    closeGame()
}

/** Some phones keep a WebView black after it goes from hidden to shown / rotates; force a fresh layout + frame. */
fun MainActivity.nudgeGameView() {
    val w = host.view
    w.visibility = View.VISIBLE
    w.requestLayout()
    w.invalidate()
    mainHandler.postDelayed({ w.translationX = 1f; mainHandler.postDelayed({ w.translationX = 0f; w.invalidate() }, 50) }, 300)
}
