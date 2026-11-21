package com.griddim.arcadegridz

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Color
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.StateListDrawable
import android.text.TextUtils
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView

fun Context.dp(v: Number): Int = (v.toFloat() * resources.displayMetrics.density + 0.5f).toInt()
fun View.dp(v: Number): Int = context.dp(v)

const val MATCH = ViewGroup.LayoutParams.MATCH_PARENT
const val WRAP = ViewGroup.LayoutParams.WRAP_CONTENT

fun lp(w: Int, h: Int, weight: Float = 0f) = LinearLayout.LayoutParams(w, h, weight)
fun flp(w: Int, h: Int, gravity: Int = Gravity.NO_GRAVITY) = FrameLayout.LayoutParams(w, h, gravity)

fun <T : ViewGroup.MarginLayoutParams> T.margins(l: Int = 0, t: Int = 0, r: Int = 0, b: Int = 0): T { setMargins(l, t, r, b); return this }

enum class Font { DISPLAY, PIXEL, BODY, BODY_BOLD }

/** Small view factory for the launcher. Everything is built in code from the current [Themes.current]. */
object Ui {
    val t get() = Themes.current

    fun rounded(fill: Int, radiusDp: Float, ctx: Context, stroke: Int = 0, strokeDp: Float = 0f): GradientDrawable =
        GradientDrawable().apply {
            shape = GradientDrawable.RECTANGLE
            cornerRadius = ctx.dp(radiusDp).toFloat()
            setColor(fill)
            if (strokeDp > 0f) setStroke(ctx.dp(strokeDp).coerceAtLeast(1), stroke)
        }

    fun gradient(colors: IntArray, radiusDp: Float, ctx: Context, orientation: GradientDrawable.Orientation = GradientDrawable.Orientation.LEFT_RIGHT) =
        GradientDrawable(orientation, colors).apply { cornerRadius = ctx.dp(radiusDp).toFloat() }

    fun neonGradient(ctx: Context, radiusDp: Float) =
        gradient(intArrayOf(t.cyan, t.violet, if (t.isNeon) 0xFFFF6FB5.toInt() else t.magenta), radiusDp, ctx)

    fun text(ctx: Context, s: CharSequence, sp: Float, color: Int = t.text, font: Font = Font.BODY, glow: Boolean = false): TextView =
        TextView(ctx).apply {
            text = s
            setTextColor(color)
            includeFontPadding = false
            when (font) {
                Font.DISPLAY -> { typeface = Themes.display(ctx); setTextSize(TypedValue.COMPLEX_UNIT_SP, sp) }
                Font.PIXEL -> { typeface = Themes.pixel(ctx); setTextSize(TypedValue.COMPLEX_UNIT_SP, Themes.pixelSize(sp)); letterSpacing = if (t.isNeon) 0.04f else 0.06f }
                Font.BODY -> { typeface = Themes.body; setTextSize(TypedValue.COMPLEX_UNIT_SP, sp) }
                Font.BODY_BOLD -> { typeface = Themes.bodyBold; setTextSize(TypedValue.COMPLEX_UNIT_SP, sp) }
            }
            if (glow && t.glow > 0.2f && !t.light) setShadowLayer(ctx.dp(8 * t.glow).toFloat(), 0f, 0f, Themes.withAlpha(color, 200))
        }

    fun oneLine(tv: TextView): TextView { tv.maxLines = 1; tv.ellipsize = TextUtils.TruncateAt.END; return tv }

    /** Neon outline button background with focus / pressed glow states (pad focus shows clearly). */
    fun buttonBackground(ctx: Context, accent: Int, radiusDp: Float = 14f, filled: Drawable? = null): StateListDrawable {
        val normal = filled ?: rounded(Themes.withAlpha(t.card, 0xE6), radiusDp, ctx, Themes.withAlpha(accent, 0x66), 2f)
        val focused = rounded(Themes.withAlpha(accent, 0x38), radiusDp, ctx, accent, 3f)
        val pressed = rounded(Themes.withAlpha(accent, 0x66), radiusDp, ctx, accent, 3f)
        return StateListDrawable().apply {
            addState(intArrayOf(android.R.attr.state_pressed), pressed)
            addState(intArrayOf(android.R.attr.state_focused), focused)
            addState(intArrayOf(android.R.attr.state_selected), focused)
            addState(intArrayOf(), normal)
        }
    }

    /** A focusable neon button: optional glyph + pixel label. */
    @SuppressLint("ClickableViewAccessibility")
    fun button(ctx: Context, label: String, glyph: String? = null, accent: Int = t.cyan, sp: Float = 10f, filled: Drawable? = null,
               labelColor: Int? = null, onClick: (View) -> Unit): NeonButton {
        val b = NeonButton(ctx)
        b.orientation = LinearLayout.HORIZONTAL
        b.gravity = Gravity.CENTER
        b.background = buttonBackground(ctx, accent, 14f, filled)
        b.setPadding(ctx.dp(16), ctx.dp(11), ctx.dp(16), ctx.dp(11))
        b.isFocusable = true
        b.isClickable = true
        b.minimumHeight = ctx.dp(44)
        if (glyph != null) {
            val g = text(ctx, glyph, sp + 5f, labelColor ?: accent, Font.BODY_BOLD)
            b.glyphView = g
            b.addView(g, lp(WRAP, WRAP).margins(r = if (label.isEmpty()) 0 else ctx.dp(9)))
        }
        if (label.isNotEmpty()) {
            val l = text(ctx, label, sp, labelColor ?: t.text, Font.PIXEL)
            l.maxLines = 1
            b.labelView = l
            b.addView(l, lp(WRAP, WRAP))
        }
        b.setOnClickListener { Sfx.play("select"); onClick(it) }
        b.onFocusChangeListener = View.OnFocusChangeListener { v, has ->
            v.animate().scaleX(if (has) 1.05f else 1f).scaleY(if (has) 1.05f else 1f).setDuration(110).start()
            if (has) Sfx.play("move")
        }
        return b
    }

    fun chip(ctx: Context, label: String, color: Int, sp: Float = 8f): TextView =
        text(ctx, label, sp, color, Font.PIXEL).apply {
            background = rounded(Themes.withAlpha(if (t.light) t.card else 0xFF050312.toInt(), 0xD9), 6f, ctx, color, 1f)
            setPadding(ctx.dp(8), ctx.dp(5), ctx.dp(8), ctx.dp(5))
            maxLines = 1
        }

    fun card(ctx: Context, radiusDp: Float = 20f, alpha: Int = 0xE6, stroke: Int = Themes.withAlpha(t.cyan, 0x55)) =
        rounded(Themes.withAlpha(t.card, alpha), radiusDp, ctx, stroke, 2f)

    fun spacer(ctx: Context, w: Int = 0, h: Int = 0) = View(ctx).apply { layoutParams = LinearLayout.LayoutParams(w, h) }

    fun transparent() = Color.TRANSPARENT
}

/** LinearLayout button so glyph + label recolour together (selected / toggled states). */
class NeonButton(ctx: Context) : LinearLayout(ctx) {
    var glyphView: TextView? = null
    var labelView: TextView? = null
    fun setLabel(s: String) { labelView?.text = s }
    fun setGlyph(s: String) { glyphView?.text = s }
    fun setAccent(accent: Int, radiusDp: Float = 14f) {
        background = Ui.buttonBackground(context, accent, radiusDp)
        glyphView?.setTextColor(accent)
    }
}
