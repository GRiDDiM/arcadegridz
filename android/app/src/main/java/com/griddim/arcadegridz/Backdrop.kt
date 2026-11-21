package com.griddim.arcadegridz

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RadialGradient
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Shader
import android.view.View
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin

/**
 * Launcher backdrop. NEON uses the 80s arcade hall art (same as the Windows app) with twinkles and a slow
 * grid pulse; the other themes draw their vector scenes (graveyard, blood and spikes, sunny day, office, bridge panels).
 * Drawn on a virtual 1600x900 stage that is cropped to fill the view.
 */
class Backdrop(ctx: Context) : View(ctx) {
    var animated = true
        set(v) { field = v; invalidate() }
    var scanlines = true
        set(v) { field = v; invalidate() }

    private var hall: Bitmap? = null
    private val p = Paint(Paint.ANTI_ALIAS_FLAG)
    private val path = Path()
    private val start = System.nanoTime()
    private val rnd = java.util.Random(7)
    private val stars = Array(70) { floatArrayOf(rnd.nextFloat() * 1600f, rnd.nextFloat() * 520f, 0.6f + rnd.nextFloat() * 2.2f, rnd.nextFloat() * 6.28f) }
    private val motes = Array(26) { floatArrayOf(rnd.nextFloat() * 1600f, rnd.nextFloat() * 900f, 0.4f + rnd.nextFloat(), rnd.nextFloat() * 6.28f) }

    init {
        try { ctx.assets.open("hall.jpg").use { hall = BitmapFactory.decodeStream(it, null, BitmapFactory.Options().apply { inSampleSize = 2 }) } } catch (_: Exception) { }
    }

    override fun onDraw(c: Canvas) {
        val t = Themes.current
        val sec = if (animated) (System.nanoTime() - start) / 1e9f else 0f
        val w = width.toFloat(); val h = height.toFloat()
        c.drawColor(t.ink)
        // virtual stage: cover-fit 1600x900
        val s = maxOf(w / 1600f, h / 900f)
        c.save()
        c.translate((w - 1600f * s) / 2f, (h - 900f * s) / 2f)
        c.scale(s, s)
        when (t.id) {
            "acid" -> acid(c, sec)
            "brutal" -> brutal(c, sec)
            "happy" -> happy(c, sec)
            "beige" -> beige(c)
            "console" -> console(c, sec)
            else -> neon(c, sec)
        }
        c.restore()
        // readability scrim
        if (!t.light) {
            p.shader = LinearGradient(0f, 0f, 0f, h, intArrayOf(Themes.withAlpha(t.ink, 0x55), Themes.withAlpha(t.ink, 0x99), Themes.withAlpha(t.ink, 0xD0)), null, Shader.TileMode.CLAMP)
            c.drawRect(0f, 0f, w, h, p); p.shader = null
        }
        if (scanlines && t.scanlines) {
            p.color = 0x22000000
            val step = dp(3).toFloat().coerceAtLeast(3f)
            var y = 0f
            while (y < h) { c.drawRect(0f, y, w, y + step / 3f, p); y += step }
        }
        if (animated && isShown) postInvalidateDelayed(33)
    }

    private fun glowCircle(c: Canvas, x: Float, y: Float, r: Float, color: Int, alpha: Int) {
        p.shader = RadialGradient(x, y, r, Themes.withAlpha(color, alpha), Themes.withAlpha(color, 0), Shader.TileMode.CLAMP)
        c.drawCircle(x, y, r, p); p.shader = null
    }

    // ===== NEON RETRO: the arcade hall art, twinkling stars, a pulsing horizon grid line =====
    private fun neon(c: Canvas, sec: Float) {
        val t = Themes.current
        val b = hall
        if (b != null) {
            val bs = maxOf(1600f / b.width, 900f / b.height)
            val bw = b.width * bs; val bh = b.height * bs
            c.drawBitmap(b, null, RectF((1600f - bw) / 2f, (900f - bh) / 2f, (1600f + bw) / 2f, (900f + bh) / 2f), p)
        } else {
            p.shader = LinearGradient(0f, 0f, 0f, 900f, intArrayOf(0xFF07051A.toInt(), 0xFF2A0B4A.toInt(), 0xFF5A1050.toInt()), null, Shader.TileMode.CLAMP)
            c.drawRect(0f, 0f, 1600f, 900f, p); p.shader = null
            glowCircle(c, 800f, 560f, 260f, 0xFFFF6FB5.toInt(), 0xAA)
            p.color = 0xFFFFB070.toInt(); c.drawCircle(800f, 560f, 150f, p)
            p.color = t.cyan; p.strokeWidth = 2f
            for (i in 0..16) { val x = i * 100f; c.drawLine(800f + (x - 800f) * 0.2f, 600f, x * 2f - 800f, 900f, p) }
        }
        for (st in stars) {
            val a = (0.5f + 0.5f * sin(sec * st[2] + st[3])) * 200
            p.color = Themes.withAlpha(0xFFFFFFFF.toInt(), a.toInt())
            c.drawCircle(st[0], st[1] * 0.55f, st[2], p)
        }
        // pulse line sweeping down the floor grid
        val k = (sec * 0.25f) % 1f
        val y = 600f + k * k * 300f
        p.color = Themes.withAlpha(t.cyan, (120 * (1 - k)).toInt()); p.strokeWidth = 3f
        c.drawLine(0f, y, 1600f, y, p)
    }

    // ===== ACID ZOMBIE: green moon over a graveyard, toxic fog, bubbles, slime drips =====
    private fun acid(c: Canvas, sec: Float) {
        val t = Themes.current
        p.shader = LinearGradient(0f, 0f, 0f, 900f, intArrayOf(0xFF020602.toInt(), 0xFF0A1F08.toInt(), 0xFF153A0E.toInt()), null, Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, 1600f, 900f, p); p.shader = null
        glowCircle(c, 1180f, 230f, 260f, t.cyan, 0x70)
        p.color = 0xFFC8FFB0.toInt(); c.drawCircle(1180f, 230f, 110f, p)
        p.color = 0x5539FF14; c.drawCircle(1150f, 210f, 22f, p); c.drawCircle(1215f, 265f, 14f, p)
        // dead trees
        p.color = 0xFF020402.toInt(); p.strokeWidth = 14f; p.strokeCap = Paint.Cap.ROUND
        c.drawLine(220f, 900f, 260f, 520f, p); c.drawLine(250f, 600f, 160f, 500f, p); c.drawLine(258f, 560f, 360f, 470f, p)
        c.drawLine(1420f, 900f, 1400f, 600f, p); c.drawLine(1404f, 660f, 1490f, 580f, p)
        // ground + tombstones with a green rim
        p.color = 0xFF030803.toInt(); c.drawRect(0f, 760f, 1600f, 900f, p)
        for (i in 0 until 7) {
            val x = 140f + i * 220f; val hh = 90f + (i % 3) * 30f
            p.color = 0xFF0B160A.toInt(); c.drawRoundRect(RectF(x, 770f - hh, x + 90f, 790f), 45f, 45f, p)
            p.style = Paint.Style.STROKE; p.strokeWidth = 3f; p.color = Themes.withAlpha(t.cyan, 0x99)
            c.drawRoundRect(RectF(x, 770f - hh, x + 90f, 790f), 45f, 45f, p); p.style = Paint.Style.FILL
        }
        // fog
        for (i in 0..2) {
            val x = ((sec * (20 + i * 9) + i * 500) % 2200f) - 300f
            glowCircle(c, x, 760f + i * 30f, 380f, t.green, 0x2A)
        }
        // bubbles
        for (m in motes) {
            val y = (m[1] - sec * 40f * m[2]) % 900f; val yy = if (y < 0) y + 900f else y
            p.style = Paint.Style.STROKE; p.strokeWidth = 2f; p.color = Themes.withAlpha(t.cyan, 0x88)
            c.drawCircle(m[0], yy, 4f + 6f * m[2], p); p.style = Paint.Style.FILL
        }
        drips(c, sec, t.cyan)
    }

    private fun drips(c: Canvas, sec: Float, color: Int) {
        p.color = Themes.withAlpha(color, 0xCC)
        path.reset(); path.moveTo(0f, 0f); path.lineTo(1600f, 0f); path.lineTo(1600f, 18f)
        var x = 1600f
        var i = 0
        while (x > 0) {
            val len = 18f + ((i * 37) % 70) + 10f * sin(sec * 0.8f + i)
            path.lineTo(x - 30f, 18f); path.quadTo(x - 45f, 18f + len, x - 60f, 18f)
            x -= 90f; i++
        }
        path.lineTo(0f, 18f); path.close(); c.drawPath(path, p)
        for (k in 0 until 5) {
            val fx = 120f + k * 330f
            val fy = ((sec * 120f + k * 170f) % 900f)
            c.drawCircle(fx, fy, 5f, p)
        }
    }

    // ===== BRUTAL: black and blood, iron spikes, a skull moon, rising embers =====
    private fun brutal(c: Canvas, sec: Float) {
        val t = Themes.current
        p.shader = LinearGradient(0f, 0f, 0f, 900f, intArrayOf(0xFF000000.toInt(), 0xFF1A0000.toInt(), 0xFF3A0303.toInt()), null, Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, 1600f, 900f, p); p.shader = null
        glowCircle(c, 800f, 330f, 320f, t.cyan, 0x55)
        // skull
        p.color = 0xFF1C0505.toInt(); c.drawCircle(800f, 300f, 120f, p); c.drawRoundRect(RectF(735f, 360f, 865f, 450f), 20f, 20f, p)
        p.color = Themes.withAlpha(t.cyan, 0xDD); c.drawCircle(755f, 300f, 30f, p); c.drawCircle(845f, 300f, 30f, p)
        path.reset(); path.moveTo(800f, 330f); path.lineTo(785f, 365f); path.lineTo(815f, 365f); path.close(); c.drawPath(path, p)
        // spikes
        p.color = 0xFF0A0A0A.toInt()
        for (i in 0 until 20) {
            val x = i * 84f; val hh = 140f + ((i * 53) % 160)
            path.reset(); path.moveTo(x, 900f); path.lineTo(x + 42f, 900f - hh); path.lineTo(x + 84f, 900f); path.close(); c.drawPath(path, p)
        }
        p.color = 0xFF2B2B2B.toInt(); p.strokeWidth = 5f
        for (i in 0 until 4) { val x = 200f + i * 400f; var y = 0f; while (y < 220f + i * 30) { c.drawCircle(x, y, 7f, p); y += 22f } }
        for (m in motes) {
            val y = (m[1] - sec * 60f * m[2]) % 900f; val yy = if (y < 0) y + 900f else y
            val a = (180 * (yy / 900f)).toInt()
            p.color = Themes.withAlpha(t.green, a)
            c.drawCircle(m[0] + 20f * sin(sec + m[3]), yy, 3f * m[2], p)
        }
    }

    // ===== HAPPY: sunny sky, smiling sun, rainbow, drifting clouds, rolling hills =====
    private fun happy(c: Canvas, sec: Float) {
        p.shader = LinearGradient(0f, 0f, 0f, 900f, intArrayOf(0xFF8FD6FF.toInt(), 0xFFCDEEFF.toInt(), 0xFFFFF8E7.toInt()), null, Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, 1600f, 900f, p); p.shader = null
        // rainbow
        val bands = intArrayOf(0xFFFF4B4B.toInt(), 0xFFFFA800.toInt(), 0xFFFFE14D.toInt(), 0xFF22C063.toInt(), 0xFF1E9BEA.toInt(), 0xFF9257F5.toInt())
        p.style = Paint.Style.STROKE; p.strokeWidth = 26f
        for ((i, col) in bands.withIndex()) { p.color = Themes.withAlpha(col, 0xB0); val r = 520f - i * 26f; c.drawArc(RectF(900f - r, 800f - r, 900f + r, 800f + r), 180f, 180f, false, p) }
        p.style = Paint.Style.FILL
        // sun with spinning rays and a face
        val sx = 260f; val sy = 200f
        p.color = 0xFFFFC93C.toInt(); p.strokeWidth = 10f; p.strokeCap = Paint.Cap.ROUND
        for (i in 0 until 12) {
            val a = sec * 0.4f + i * (PI.toFloat() / 6f)
            c.drawLine(sx + cos(a) * 120f, sy + sin(a) * 120f, sx + cos(a) * 165f, sy + sin(a) * 165f, p)
        }
        p.color = 0xFFFFD84D.toInt(); c.drawCircle(sx, sy, 100f, p)
        p.color = 0xFF3A2A5C.toInt(); c.drawCircle(sx - 32f, sy - 15f, 9f, p); c.drawCircle(sx + 32f, sy - 15f, 9f, p)
        p.style = Paint.Style.STROKE; p.strokeWidth = 7f; c.drawArc(RectF(sx - 40f, sy - 20f, sx + 40f, sy + 45f), 20f, 140f, false, p); p.style = Paint.Style.FILL
        // clouds
        p.color = 0xF2FFFFFF.toInt()
        for (i in 0 until 4) {
            val x = ((sec * (12 + i * 5) + i * 450) % 2000f) - 200f; val y = 120f + i * 70f
            c.drawCircle(x, y, 50f, p); c.drawCircle(x + 55f, y - 22f, 62f, p); c.drawCircle(x + 115f, y, 48f, p); c.drawRect(x, y, x + 115f, y + 48f, p)
        }
        // hills + flowers
        p.color = 0xFF7EDB7A.toInt(); c.drawOval(RectF(-300f, 690f, 900f, 1200f), p)
        p.color = 0xFF5FCB6A.toInt(); c.drawOval(RectF(600f, 720f, 1900f, 1250f), p)
        for (i in 0 until 14) {
            val x = 60f + i * 112f; val y = 820f + (i % 3) * 22f
            p.color = bands[i % bands.size]; c.drawCircle(x, y, 10f, p); p.color = 0xFFFFF3A0.toInt(); c.drawCircle(x, y, 4f, p)
        }
        // sparkles and hearts
        for (m in motes.take(12)) {
            val a = (0.5f + 0.5f * sin(sec * 2f * m[2] + m[3]))
            p.color = Themes.withAlpha(0xFFFF4F98.toInt(), (220 * a).toInt())
            val x = m[0]; val y = m[1] * 0.7f
            c.drawCircle(x - 6f, y, 7f, p); c.drawCircle(x + 6f, y, 7f, p)
            path.reset(); path.moveTo(x - 13f, y + 2f); path.lineTo(x, y + 16f); path.lineTo(x + 13f, y + 2f); path.close(); c.drawPath(path, p)
        }
    }

    // ===== BEIGE: nothing happens here. Ever. =====
    private fun beige(c: Canvas) {
        c.drawColor(0xFFE7DECC.toInt())
        p.color = 0xFFD9CFBB.toInt(); p.strokeWidth = 3f
        var x = 0f; while (x <= 1600f) { c.drawLine(x, 0f, x, 900f, p); x += 160f }
        var y = 0f; while (y <= 900f) { c.drawLine(0f, y, 1600f, y, p); y += 160f }
    }

    // ===== STAR CONSOLE: black void framed by rounded bridge-panel bars and numbered blocks =====
    private fun console(c: Canvas, sec: Float) {
        val t = Themes.current
        c.drawColor(0xFF000000.toInt())
        for (st in stars) {
            val a = (0.4f + 0.6f * sin(sec * 0.6f * st[2] + st[3])) * 160
            p.color = Themes.withAlpha(0xFFFFFFFF.toInt(), a.toInt()); c.drawCircle(st[0], st[1] * 1.7f, st[2] * 0.8f, p)
        }
        val orange = t.cyan; val lilac = t.violet; val pink = t.magenta
        p.color = orange
        c.drawRoundRect(RectF(30f, 30f, 520f, 110f), 40f, 40f, p)
        c.drawRect(30f, 70f, 150f, 860f, p)
        c.drawRoundRect(RectF(30f, 790f, 520f, 870f), 40f, 40f, p)
        p.color = lilac; c.drawRoundRect(RectF(540f, 30f, 1000f, 70f), 20f, 20f, p)
        p.color = pink; c.drawRoundRect(RectF(1020f, 30f, 1570f, 70f), 20f, 20f, p)
        p.color = lilac; c.drawRoundRect(RectF(540f, 830f, 1200f, 870f), 20f, 20f, p)
        p.color = t.gold; c.drawRoundRect(RectF(1220f, 830f, 1570f, 870f), 20f, 20f, p)
        val cols = intArrayOf(t.gold, lilac, pink, orange, t.green)
        for (i in 0 until 5) { p.color = cols[i]; c.drawRect(40f, 140f + i * 120f, 140f, 240f + i * 120f, p) }
        // blinking blocks on the right
        for (i in 0 until 8) {
            val on = ((sec * 1.5f + i * 0.7f).toInt() % 3) != 0
            p.color = Themes.withAlpha(cols[i % cols.size], if (on) 0xFF else 0x40)
            c.drawRoundRect(RectF(1530f, 120f + i * 80f, 1575f, 180f + i * 80f), 10f, 10f, p)
        }
        val k = (sec * 0.15f) % 1f
        p.color = Themes.withAlpha(orange, 0x40); p.strokeWidth = 2f
        c.drawLine(160f, 100f + k * 720f, 1520f, 100f + k * 720f, p)
    }

    @Suppress("unused") private val tmp = Rect()
}
