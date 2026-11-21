package com.griddim.arcadegridz

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface

/** One launcher theme (same six as the Windows ag app): palette, fonts and backdrop flags. */
data class AgTheme(
    val id: String, val name: String, val tagline: String,
    val ink: Int, val card: Int, val text: Int, val muted: Int, val dim: Int,
    val cyan: Int, val magenta: Int, val violet: Int, val gold: Int, val green: Int, val danger: Int,
    val light: Boolean, val glow: Float, val scanlines: Boolean,
    val displayFont: String?, val uiFont: String?, val emoji: String, val uiScale: Float = 1f
) {
    val isNeon get() = id == "neon"
    val swatches get() = intArrayOf(ink, cyan, magenta, violet, gold, green)
}

object Themes {
    private fun c(argb: Long) = argb.toInt()

    private const val BUNGEE = "fonts/Bungee-Regular.ttf"
    private const val PRESS = "fonts/PressStart2P-Regular.ttf"

    val all = listOf(
        AgTheme("neon", "NEON RETRO", "The original 80s arcade glow.",
            c(0xFF07051A), c(0xFF0B0820), c(0xFFE8ECFF), c(0xFF9FB0D8), c(0xFF6C7699),
            c(0xFF5EE7FF), c(0xFFFF3AA7), c(0xFF9F7CFF), c(0xFFFFC14D), c(0xFF3AFF9A), c(0xFFFF5468),
            false, 1f, true, BUNGEE, PRESS, "🕹️"),
        AgTheme("acid", "ACID ZOMBIE", "Toxic green neon from the graveyard.",
            c(0xFF030803), c(0xFF071107), c(0xFFE4FFD8), c(0xFF97C893), c(0xFF5B7A58),
            c(0xFF39FF14), c(0xFFB6FF00), c(0xFF00FFA3), c(0xFFE8FF3A), c(0xFF7CFF4F), c(0xFFC04BFF),
            false, 1f, true, "fonts/Creepster-Regular.ttf", PRESS, "🧟"),
        AgTheme("brutal", "BRUTAL", "Blood red. Black metal. No mercy.",
            c(0xFF060000), c(0xFF120303), c(0xFFF3E4E4), c(0xFFB89A9A), c(0xFF745656),
            c(0xFFE00000), c(0xFF9A0000), c(0xFFFF3D1F), c(0xFFC9C9C9), c(0xFFFF5A1F), c(0xFFFF1A1A),
            false, 1f, true, "fonts/MetalMania-Regular.ttf", "fonts/BebasNeue-Regular.ttf", "🤘", 1.6f),
        AgTheme("happy", "HAPPY", "Sunny day, rainbows and unicorns.",
            c(0xFFFFF8E7), c(0xFFFFFFFF), c(0xFF3A2A5C), c(0xFF6B5B8F), c(0xFF9A8FB5),
            c(0xFF1E9BEA), c(0xFFFF4F98), c(0xFF9257F5), c(0xFFFFA800), c(0xFF22C063), c(0xFFFF4B4B),
            true, 0.55f, false, "fonts/Chewy-Regular.ttf", "fonts/Chewy-Regular.ttf", "🦄", 1.45f),
        AgTheme("beige", "BEIGE", "Professionally boring. Like a tax form.",
            c(0xFFE7DECC), c(0xFFF1EBDE), c(0xFF4A4237), c(0xFF6E6557), c(0xFF938A7A),
            c(0xFF8C7B62), c(0xFF94866F), c(0xFF8F8573), c(0xFF9C8C6C), c(0xFF8A8270), c(0xFF8A6F60),
            true, 0.12f, false, null, null, "📎", 1.3f),
        AgTheme("console", "STAR CONSOLE", "Starship bridge panels: rounded bars, black space.",
            c(0xFF000000), c(0xFF05050A), c(0xFFFFCC99), c(0xFFCC99CC), c(0xFF9999CC),
            c(0xFFFF9900), c(0xFFCC6699), c(0xFF9999FF), c(0xFFFFCC66), c(0xFF99CCFF), c(0xFFCC4444),
            false, 0.25f, false, "fonts/BebasNeue-Regular.ttf", "fonts/BebasNeue-Regular.ttf", "🖖", 1.7f),
    )

    fun get(id: String?) = all.firstOrNull { it.id == id } ?: all[0]

    var current: AgTheme = all[0]

    private val cache = HashMap<String, Typeface>()
    private fun load(ctx: Context, path: String?, fallback: Typeface): Typeface {
        if (path == null) return fallback
        return cache.getOrPut(path) {
            try { Typeface.createFromAsset(ctx.assets, path) } catch (_: Exception) { fallback }
        }
    }

    fun display(ctx: Context): Typeface = load(ctx, current.displayFont, Typeface.create(Typeface.SANS_SERIF, Typeface.BOLD))
    fun pixel(ctx: Context): Typeface = load(ctx, current.uiFont, Typeface.create(Typeface.SANS_SERIF, Typeface.BOLD))
    val body: Typeface get() = Typeface.create("sans-serif", Typeface.NORMAL)
    val bodyBold: Typeface get() = Typeface.create("sans-serif-medium", Typeface.NORMAL)

    /** Pixel-font sizes are authored for Press Start 2P; condensed replacements are scaled up to match. */
    fun pixelSize(sp: Float) = if (current.isNeon || current.uiFont == "fonts/PressStart2P-Regular.ttf") sp else sp * current.uiScale

    fun withAlpha(color: Int, alpha: Int) = Color.argb(alpha.coerceIn(0, 255), Color.red(color), Color.green(color), Color.blue(color))

    fun statusColor(status: String?): Int = when ((status ?: "").lowercase()) {
        "beta" -> current.cyan
        "alpha" -> if (current.isNeon) c(0xFFFFB070) else current.gold
        "release", "live", "stable" -> current.green
        else -> current.violet
    }

    fun tagColor(tag: String): Int = when (tag) {
        "VERIFIED" -> current.green
        "ASSISTED" -> if (current.isNeon) c(0xFFFFB070) else current.gold
        "CHEATER" -> current.danger
        else -> current.dim
    }
}
