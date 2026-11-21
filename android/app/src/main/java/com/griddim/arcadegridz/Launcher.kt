package com.griddim.arcadegridz

import android.annotation.SuppressLint
import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.Outline
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.text.format.DateFormat
import android.view.Gravity
import android.view.InputDevice
import android.view.View
import android.view.ViewGroup
import android.view.ViewOutlineProvider
import android.widget.AbsListView
import android.widget.AdapterView
import android.widget.BaseAdapter
import android.widget.FrameLayout
import android.widget.GridView
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.TextView
import java.util.Date

/** View references for the launcher that is currently built (rebuilt on theme / layout changes). */
class LauncherViews {
    var grid: GridView? = null
    var adapter: TileAdapter? = null
    var categoryLabel: TextView? = null
    var testing: NeonButton? = null
    var story: NeonButton? = null
    var clock: TextView? = null
    var network: TextView? = null
    var signIn: View? = null
    var accountBtn: View? = null
    var accountName: TextView? = null
    var accountInitial: TextView? = null
    var accountAvatar: ImageView? = null
    var updateChip: View? = null
    var updateVersion: TextView? = null
    var bannerFrame: View? = null
    var bannerA: ImageView? = null
    var bannerB: ImageView? = null
    // details panel
    var heroArt: ImageView? = null
    var previewHost: FrameLayout? = null
    var heroName: TextView? = null
    var heroStatus: TextView? = null
    var heroMeta: TextView? = null
    var heroDesc: TextView? = null
    var play: NeonButton? = null
    var trailer: NeonButton? = null
    var scoreHeader: TextView? = null
    var scoreEmpty: TextView? = null
    var scoreList: LinearLayout? = null
    // music bar
    var musicTitle: TextView? = null
    var musicAlbum: TextView? = null
    var musicTime: TextView? = null
    var musicSeek: SeekBar? = null
    var musicPlay: NeonButton? = null
    var musicShuffle: NeonButton? = null
    var musicRepeat: NeonButton? = null
    var musicMute: NeonButton? = null
    var musicAlbumBtn: NeonButton? = null
    var musicVolume: SeekBar? = null
    var seeking = false
    var detailsSheet: View? = null

    fun clear() {
        grid = null; adapter = null; categoryLabel = null; testing = null; story = null; clock = null; network = null
        signIn = null; accountBtn = null; accountName = null; accountInitial = null; accountAvatar = null; updateChip = null; updateVersion = null
        bannerFrame = null; bannerA = null; bannerB = null
        clearDetails()
        musicTitle = null; musicAlbum = null; musicTime = null; musicSeek = null; musicPlay = null; musicShuffle = null; musicRepeat = null
        musicMute = null; musicAlbumBtn = null; musicVolume = null; detailsSheet = null
    }

    fun clearDetails() {
        heroArt = null; previewHost = null; heroName = null; heroStatus = null; heroMeta = null; heroDesc = null
        play = null; trailer = null; scoreHeader = null; scoreEmpty = null; scoreList = null
    }
}

/** A card that keeps a fixed aspect ratio (cover tiles are 224x300 in the Windows app). */
class AspectFrame(ctx: Context, private val ratio: Float) : FrameLayout(ctx) {
    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val w = MeasureSpec.getSize(widthMeasureSpec)
        super.onMeasure(MeasureSpec.makeMeasureSpec(w, MeasureSpec.EXACTLY), MeasureSpec.makeMeasureSpec((w * ratio).toInt(), MeasureSpec.EXACTLY))
    }
}

fun View.roundClip(radiusPx: Float) {
    outlineProvider = object : ViewOutlineProvider() {
        override fun getOutline(view: View, outline: Outline) { outline.setRoundRect(0, 0, view.width, view.height, radiusPx) }
    }
    clipToOutline = true
}

class TileAdapter(private val a: MainActivity) : BaseAdapter() {
    override fun getCount() = a.visible.size
    override fun getItem(position: Int) = a.visible[position]
    override fun getItemId(position: Int) = position.toLong()

    override fun getView(position: Int, convertView: View?, parent: ViewGroup): View {
        val g = a.visible[position]
        val t = Themes.current
        val ctx = parent.context
        val tile = AspectFrame(ctx, 1.34f)
        tile.layoutParams = AbsListView.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        tile.tag = g.id
        val radius = ctx.dp(16).toFloat()
        tile.background = Ui.rounded(t.card, 16f, ctx, Themes.withAlpha(t.cyan, 0x55), 2f)
        tile.roundClip(radius)
        tile.alpha = if (g.playable) 1f else 0.55f
        val art = ImageView(ctx)
        art.scaleType = ImageView.ScaleType.CENTER_CROP
        tile.addView(art, flp(MATCH, MATCH))
        Images.load(g.art, art, 640)
        val scrim = View(ctx)
        scrim.background = GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, intArrayOf(0x00000000, 0x00000000, 0xC0050312.toInt()))
        tile.addView(scrim, flp(MATCH, MATCH))
        val status = Ui.chip(ctx, g.status.uppercase(), Themes.statusColor(g.status), 7f)
        tile.addView(status, flp(WRAP, WRAP, Gravity.TOP or Gravity.START).margins(ctx.dp(10), ctx.dp(10)))
        if (!g.playable) tile.addView(Ui.chip(ctx, "LOCKED", t.danger, 7f), flp(WRAP, WRAP, Gravity.TOP or Gravity.END).margins(0, ctx.dp(10), ctx.dp(10)))
        val bottom = LinearLayout(ctx)
        bottom.orientation = LinearLayout.VERTICAL
        bottom.setPadding(ctx.dp(12), 0, ctx.dp(12), ctx.dp(12))
        bottom.addView(Ui.oneLine(Ui.text(ctx, g.genre.uppercase(), 6.5f, if (t.light) Color.WHITE else t.cyan, Font.PIXEL)))
        val name = Ui.text(ctx, g.name, if (a.isTablet) 17f else 15f, Color.WHITE, Font.DISPLAY)
        name.maxLines = 2
        name.ellipsize = android.text.TextUtils.TruncateAt.END
        name.setShadowLayer(ctx.dp(3).toFloat(), ctx.dp(1.5).toFloat(), ctx.dp(1.5).toFloat(), 0xE6000000.toInt())
        bottom.addView(name, lp(MATCH, WRAP).margins(t = ctx.dp(5)))
        tile.addView(bottom, flp(MATCH, WRAP, Gravity.BOTTOM))
        tile.foreground = highlight(ctx, g.id == a.selected?.id)
        return tile
    }

    fun highlight(ctx: Context, on: Boolean) =
        if (!on) null else Ui.rounded(Color.TRANSPARENT, 16f, ctx, Themes.current.cyan, 3f)
}

// ===================================================================== build

fun MainActivity.buildLauncher() {
    stopPreview()
    bannerRun?.let { mainHandler.removeCallbacks(it) }
    launcherHost.removeAllViews()
    v.clear()
    val col = LinearLayout(this)
    col.orientation = LinearLayout.VERTICAL
    if (isTablet) buildTablet(col) else buildPhone(col)
    launcherHost.addView(col, flp(MATCH, MATCH))
    rebuildCatalogViews()
    refreshHeader()
    refreshMusic()
    if (isTablet) loadBanners()
    mainHandler.post { restoreFocus() }
}

private fun MainActivity.logoView(heightDp: Int): ImageView {
    val logo = ImageView(this)
    logo.adjustViewBounds = true
    logo.scaleType = ImageView.ScaleType.FIT_START
    try { assets.open("logo.png").use { logo.setImageBitmap(android.graphics.BitmapFactory.decodeStream(it)) } } catch (_: Exception) { }
    logo.layoutParams = lp(WRAP, dp(heightDp))
    return logo
}

private fun MainActivity.categorySwitcher(compact: Boolean): LinearLayout {
    val t = Themes.current
    val row = LinearLayout(this)
    row.orientation = LinearLayout.HORIZONTAL
    row.gravity = Gravity.CENTER_VERTICAL
    row.addView(Ui.button(this, if (compact) "" else "LB", "◀", t.cyan, 9f) { changeCategory(-1) }, lp(WRAP, WRAP))
    val pill = FrameLayout(this)
    pill.background = Ui.neonGradient(this, 26f)
    pill.setPadding(dp(22), dp(10), dp(22), dp(10))
    val label = Ui.text(this, "ALL GAMES", if (compact) 17f else 21f, t.ink, Font.DISPLAY)
    label.gravity = Gravity.CENTER
    label.maxLines = 1
    label.letterSpacing = 0.12f
    v.categoryLabel = label
    pill.addView(label, flp(MATCH, WRAP, Gravity.CENTER))
    row.addView(pill, (if (compact) lp(0, WRAP, 1f) else lp(dp(300), WRAP)).margins(l = dp(8), r = dp(8)))
    row.addView(Ui.button(this, if (compact) "" else "RB", "▶", t.cyan, 9f) { changeCategory(1) }, lp(WRAP, WRAP))
    return row
}

private fun MainActivity.accountButtons(compact: Boolean): LinearLayout {
    val t = Themes.current
    val row = LinearLayout(this)
    row.orientation = LinearLayout.HORIZONTAL
    row.gravity = Gravity.CENTER_VERTICAL or Gravity.END
    val signIn = Ui.button(this, "SIGN IN", "⎆", t.cyan, if (compact) 8f else 9f) { showWelcome(fromLauncher = true) }
    v.signIn = signIn
    row.addView(signIn, lp(WRAP, WRAP))
    // signed in: avatar + name; opens the in-app account panel
    val acct = NeonButton(this)
    acct.orientation = LinearLayout.HORIZONTAL
    acct.gravity = Gravity.CENTER_VERTICAL
    acct.background = Ui.buttonBackground(this, t.cyan, 24f)
    acct.setPadding(dp(5), dp(5), dp(if (compact) 5 else 14), dp(5))
    acct.isFocusable = true
    acct.isClickable = true
    acct.setOnClickListener { Sfx.play("select"); showAccount() }
    val av = FrameLayout(this)
    val circle = View(this)
    circle.background = GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, intArrayOf(t.cyan, t.violet, t.magenta)).apply { shape = GradientDrawable.OVAL }
    av.addView(circle, flp(MATCH, MATCH))
    val initial = Ui.text(this, "?", 14f, t.ink, Font.DISPLAY)
    initial.gravity = Gravity.CENTER
    av.addView(initial, flp(MATCH, MATCH))
    val img = ImageView(this)
    img.scaleType = ImageView.ScaleType.CENTER_CROP
    img.visibility = View.GONE
    av.addView(img, flp(MATCH, MATCH))
    av.roundClip(dp(17).toFloat())
    acct.addView(av, lp(dp(34), dp(34)))
    val name = Ui.oneLine(Ui.text(this, "", 11f, t.text, Font.PIXEL))
    name.maxWidth = dp(180)
    if (!compact) acct.addView(name, lp(WRAP, WRAP).margins(l = dp(10)))
    v.accountBtn = acct; v.accountName = name; v.accountInitial = initial; v.accountAvatar = img
    row.addView(acct, lp(WRAP, WRAP))
    return row
}

private fun MainActivity.navButtons(row: LinearLayout, sp: Float, withExit: Boolean) {
    val t = Themes.current
    row.addView(Ui.button(this, "GAMES", "▦", t.cyan, sp) { storyMode = false; albumMode = false; applyFilter(); restoreFocus() }, lp(WRAP, WRAP).margins(r = dp(10)))
    row.addView(Ui.button(this, "HI-SCORES", "♛", t.gold, sp) { showScores() }, lp(WRAP, WRAP).margins(r = dp(10)))
    val story = Ui.button(this, "STORYBOARDS", "▤", t.magenta, sp) { storyMode = !storyMode; albumMode = false; applyFilter(); restoreFocus() }
    v.story = story
    row.addView(story, lp(WRAP, WRAP).margins(r = dp(10)))
    row.addView(Ui.button(this, "OPTIONS", "⚙", t.violet, sp) { showOptions() }, lp(WRAP, WRAP).margins(r = dp(10)))
    val testing = Ui.button(this, "LIVE ONLY", "◎", t.green, sp) { toggleTesting() }
    v.testing = testing
    row.addView(testing, lp(WRAP, WRAP).margins(r = dp(10)))
    if (withExit) row.addView(Ui.button(this, "EXIT", "✕", t.danger, sp) { finishAffinity() }, lp(WRAP, WRAP))
}

private fun MainActivity.buildGrid(columns: Int): GridView {
    val grid = GridView(this)
    grid.numColumns = columns
    grid.stretchMode = GridView.STRETCH_COLUMN_WIDTH
    grid.horizontalSpacing = dp(if (isTablet) 16 else 10)
    grid.verticalSpacing = dp(if (isTablet) 16 else 10)
    grid.setPadding(dp(10), dp(8), dp(10), dp(16))
    grid.clipToPadding = false
    grid.selector = ColorDrawable(Color.TRANSPARENT)
    grid.isVerticalScrollBarEnabled = false
    grid.isFocusable = true
    val adapter = TileAdapter(this)
    grid.adapter = adapter
    grid.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
        override fun onItemSelected(p: AdapterView<*>?, view: View?, position: Int, id: Long) {
            visible.getOrNull(position)?.let { if (it.id != selected?.id) { Sfx.play("move"); selectGame(it) } }
        }
        override fun onNothingSelected(p: AdapterView<*>?) {}
    }
    grid.setOnItemClickListener { _, _, position, _ ->
        val g = visible.getOrNull(position) ?: return@setOnItemClickListener
        val now = System.currentTimeMillis()
        if (!isTablet) { selectGame(g); showDetailsSheet(g); return@setOnItemClickListener }
        if (grid.isInTouchMode) {
            if (g.id == selected?.id && now - lastTileClick < 500) launch(g) else { Sfx.play("move"); selectGame(g) }
        } else { selectGame(g); focusPlay() }
        lastTileClick = now
    }
    v.grid = grid
    v.adapter = adapter
    return grid
}

private fun MainActivity.buildTablet(col: LinearLayout) {
    val t = Themes.current
    // header: logo / genre switcher / clock + account
    val header = LinearLayout(this)
    header.orientation = LinearLayout.HORIZONTAL
    header.gravity = Gravity.CENTER_VERTICAL
    header.setPadding(dp(26), dp(14), dp(26), dp(6))
    val left = FrameLayout(this)
    left.addView(logoView(62), flp(WRAP, dp(62), Gravity.CENTER_VERTICAL or Gravity.START))
    header.addView(left, lp(0, WRAP, 1f))
    header.addView(categorySwitcher(compact = false), lp(WRAP, WRAP))
    val right = LinearLayout(this)
    right.orientation = LinearLayout.VERTICAL
    right.gravity = Gravity.END
    val r1 = LinearLayout(this)
    r1.orientation = LinearLayout.HORIZONTAL
    r1.gravity = Gravity.CENTER_VERTICAL or Gravity.END
    r1.addView(updateChip(), lp(WRAP, WRAP).margins(r = dp(12)))
    r1.addView(neonDot(t.green), lp(dp(8), dp(8)).margins(r = dp(7)))
    val net = Ui.oneLine(Ui.text(this, "SYNCING GRID...", 7f, t.violet, Font.PIXEL))
    v.network = net
    r1.addView(net, lp(WRAP, WRAP).margins(r = dp(14)))
    val clock = Ui.text(this, "--:--", 26f, t.text, Font.DISPLAY, glow = true)
    v.clock = clock
    r1.addView(clock)
    right.addView(r1, lp(MATCH, WRAP))
    val r2 = LinearLayout(this)
    r2.orientation = LinearLayout.HORIZONTAL
    r2.gravity = Gravity.CENTER_VERTICAL or Gravity.END
    r2.addView(accountButtons(compact = false))
    right.addView(r2, lp(MATCH, WRAP).margins(t = dp(4)))
    header.addView(right, lp(0, WRAP, 1f))
    col.addView(header, lp(MATCH, WRAP))

    // main nav
    val navScroll = HorizontalScrollView(this)
    navScroll.isHorizontalScrollBarEnabled = false
    val nav = LinearLayout(this)
    nav.orientation = LinearLayout.HORIZONTAL
    nav.setPadding(dp(22), dp(6), dp(22), dp(6))
    navButtons(nav, 10f, withExit = true)
    navScroll.addView(nav)
    col.addView(navScroll, lp(MATCH, WRAP))

    // game grid + banner + details
    val body = LinearLayout(this)
    body.orientation = LinearLayout.HORIZONTAL
    body.setPadding(dp(14), dp(6), dp(22), dp(8))
    val cols = if (isLandscape) (if (resources.configuration.screenWidthDp >= 1100) 4 else 3) else 2
    body.addView(buildGrid(cols), lp(0, MATCH, 1.15f))
    // banner slot: IAB wide skyscraper 160x600, rotated from arcadegridz.com/assets/banners/banners.json
    if (isLandscape) {
        val bf = FrameLayout(this)
        bf.visibility = View.GONE
        val inner = AspectFrame(this, 600f / 160f)
        inner.background = Ui.rounded(t.card, 10f, this, Themes.withAlpha(t.cyan, 0x44), 1f)
        inner.roundClip(dp(10).toFloat())
        val a = ImageView(this).apply { scaleType = ImageView.ScaleType.CENTER_CROP }
        val b = ImageView(this).apply { scaleType = ImageView.ScaleType.CENTER_CROP; alpha = 0f }
        inner.addView(a, flp(MATCH, MATCH)); inner.addView(b, flp(MATCH, MATCH))
        inner.addView(Ui.text(this, "AD", 5f, 0x99FFFFFF.toInt(), Font.PIXEL), flp(WRAP, WRAP, Gravity.BOTTOM or Gravity.END).margins(0, 0, dp(5), dp(4)))
        inner.setOnClickListener { bannerTapped() }
        bf.addView(inner, flp(MATCH, WRAP))
        v.bannerFrame = bf; v.bannerA = a; v.bannerB = b
        body.addView(bf, lp(dp(118), MATCH).margins(l = dp(6), r = dp(6), t = dp(8)))
    }
    val detailsScroll = ScrollView(this)
    detailsScroll.isVerticalScrollBarEnabled = false
    val details = LinearLayout(this)
    details.orientation = LinearLayout.VERTICAL
    details.setPadding(dp(16), dp(6), 0, dp(10))
    buildDetails(details, heroHeightDp = if (isLandscape) 190 else 240)
    detailsScroll.addView(details, flp(MATCH, WRAP))
    body.addView(detailsScroll, lp(0, MATCH, if (isLandscape) 0.85f else 1f))
    col.addView(body, lp(MATCH, 0, 1f))

    col.addView(musicBar(full = true), lp(MATCH, dp(76)))
}

private fun MainActivity.buildPhone(col: LinearLayout) {
    val t = Themes.current
    if (isLandscape) {
        val top = LinearLayout(this)
        top.orientation = LinearLayout.HORIZONTAL
        top.gravity = Gravity.CENTER_VERTICAL
        top.setPadding(dp(14), dp(8), dp(14), dp(2))
        top.addView(logoView(36), lp(WRAP, dp(36)).margins(r = dp(10)))
        top.addView(categorySwitcher(compact = true), lp(0, WRAP, 1f))
        top.addView(updateChip(), lp(WRAP, WRAP).margins(l = dp(8)))
        top.addView(accountButtons(compact = true), lp(WRAP, WRAP).margins(l = dp(8)))
        col.addView(top, lp(MATCH, WRAP))
    } else {
        val top = LinearLayout(this)
        top.orientation = LinearLayout.HORIZONTAL
        top.gravity = Gravity.CENTER_VERTICAL
        top.setPadding(dp(14), dp(14), dp(14), dp(4))
        top.addView(logoView(42), lp(0, dp(42), 1f))
        top.addView(updateChip(), lp(WRAP, WRAP).margins(r = dp(8)))
        top.addView(accountButtons(compact = true), lp(WRAP, WRAP))
        col.addView(top, lp(MATCH, WRAP))
        val net = Ui.oneLine(Ui.text(this, "SYNCING GRID...", 7f, t.violet, Font.PIXEL))
        v.network = net
        col.addView(net, lp(MATCH, WRAP).margins(l = dp(16), t = dp(2), b = dp(6)))
        val cat = categorySwitcher(compact = true)
        cat.setPadding(dp(12), 0, dp(12), 0)
        col.addView(cat, lp(MATCH, WRAP))
    }
    val navScroll = HorizontalScrollView(this)
    navScroll.isHorizontalScrollBarEnabled = false
    val nav = LinearLayout(this)
    nav.orientation = LinearLayout.HORIZONTAL
    nav.setPadding(dp(12), dp(8), dp(12), dp(6))
    navButtons(nav, 8f, withExit = false)
    navScroll.addView(nav)
    col.addView(navScroll, lp(MATCH, WRAP))
    col.addView(buildGrid(if (isLandscape) 4 else 2), lp(MATCH, 0, 1f).margins(l = dp(4), r = dp(4)))
    col.addView(musicBar(full = false), lp(MATCH, dp(58)))
}

private fun MainActivity.updateChip(): View {
    val t = Themes.current
    val b = NeonButton(this)
    b.orientation = LinearLayout.VERTICAL
    b.background = Ui.buttonBackground(this, t.gold, 12f)
    b.setPadding(dp(10), dp(6), dp(10), dp(6))
    b.isFocusable = true; b.isClickable = true
    b.addView(Ui.text(this, "⬆ UPDATE READY", 7.5f, t.gold, Font.PIXEL))
    val ver = Ui.text(this, "", 6f, t.dim, Font.PIXEL)
    b.addView(ver, lp(WRAP, WRAP).margins(t = dp(3)))
    b.setOnClickListener { Sfx.play("select"); showUpdatePrompt() }
    b.visibility = View.GONE
    v.updateChip = b; v.updateVersion = ver
    return b
}

/** Hero art, name, status, description, PLAY / TRAILER and the game's top scores. */
fun MainActivity.buildDetails(c: LinearLayout, heroHeightDp: Int) {
    val t = Themes.current
    val heroFrame = FrameLayout(this)
    heroFrame.background = Ui.rounded(t.card, 20f, this, Themes.withAlpha(t.cyan, 0x66), 2f)
    heroFrame.roundClip(dp(20).toFloat())
    val art = ImageView(this)
    art.scaleType = ImageView.ScaleType.CENTER_CROP
    heroFrame.addView(art, flp(MATCH, MATCH))
    val pv = FrameLayout(this)
    pv.visibility = View.GONE
    heroFrame.addView(pv, flp(MATCH, MATCH))
    val scrim = View(this)
    scrim.background = GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, intArrayOf(0, 0, 0x99050312.toInt()))
    heroFrame.addView(scrim, flp(MATCH, MATCH))
    c.addView(heroFrame, lp(MATCH, dp(heroHeightDp)))
    val name = Ui.text(this, "", if (isTablet) 32f else 27f, if (t.light) t.text else Color.WHITE, Font.DISPLAY, glow = true)
    name.maxLines = 2
    name.ellipsize = android.text.TextUtils.TruncateAt.END
    c.addView(name, lp(MATCH, WRAP).margins(t = dp(14), b = dp(6)))
    val row = LinearLayout(this)
    row.orientation = LinearLayout.HORIZONTAL
    row.gravity = Gravity.CENTER_VERTICAL
    val status = Ui.chip(this, "", t.cyan, 8f)
    row.addView(status, lp(WRAP, WRAP).margins(r = dp(10)))
    val meta = Ui.oneLine(Ui.text(this, "", 8.5f, t.cyan, Font.PIXEL))
    row.addView(meta, lp(0, WRAP, 1f))
    c.addView(row, lp(MATCH, WRAP))
    val desc = Ui.text(this, "", 15f, t.muted, Font.BODY)
    desc.setLineSpacing(dp(3).toFloat(), 1f)
    desc.maxLines = if (isTablet) 4 else 8
    desc.ellipsize = android.text.TextUtils.TruncateAt.END
    c.addView(desc, lp(MATCH, WRAP).margins(t = dp(12), b = dp(12)))
    val buttons = LinearLayout(this)
    buttons.orientation = LinearLayout.HORIZONTAL
    val playBg = Ui.gradient(intArrayOf(t.green, t.cyan), 14f, this)
    val play = Ui.button(this, "PLAY", "▶", t.green, 12f, filled = playBg, labelColor = t.ink) { selected?.let { launch(it) } }
    play.minimumWidth = dp(170)
    buttons.addView(play, lp(WRAP, WRAP).margins(r = dp(12)))
    val trailer = Ui.button(this, "TRAILER", "▣", t.magenta, 10f) { selected?.let { launch(it, trailer = true) } }
    trailer.visibility = View.GONE
    buttons.addView(trailer, lp(WRAP, WRAP))
    c.addView(buttons, lp(MATCH, WRAP))
    // high scores box
    val box = LinearLayout(this)
    box.orientation = LinearLayout.VERTICAL
    box.background = Ui.rounded(Themes.withAlpha(t.card, 0xB3), 14f, this, Themes.withAlpha(t.gold, 0x33), 1f)
    box.setPadding(dp(16), dp(12), dp(16), dp(12))
    val head = Ui.text(this, "♛  HIGH SCORES", 10f, t.gold, Font.PIXEL, glow = true)
    box.addView(head)
    val empty = Ui.text(this, "NO SCORES YET. BE THE FIRST.", 8f, t.dim, Font.PIXEL)
    empty.visibility = View.GONE
    box.addView(empty, lp(WRAP, WRAP).margins(t = dp(10)))
    val list = LinearLayout(this)
    list.orientation = LinearLayout.VERTICAL
    box.addView(list, lp(MATCH, WRAP).margins(t = dp(6)))
    c.addView(box, lp(MATCH, WRAP).margins(t = dp(14)))
    v.heroArt = art; v.previewHost = pv; v.heroName = name; v.heroStatus = status; v.heroMeta = meta; v.heroDesc = desc
    v.play = play; v.trailer = trailer; v.scoreHeader = head; v.scoreEmpty = empty; v.scoreList = list
}

@SuppressLint("SetTextI18n")
private fun MainActivity.musicBar(full: Boolean): View {
    val t = Themes.current
    val bar = LinearLayout(this)
    bar.orientation = LinearLayout.HORIZONTAL
    bar.gravity = Gravity.CENTER_VERTICAL
    bar.setBackgroundColor(Themes.withAlpha(if (t.light) t.card else 0xFF050312.toInt(), 0xE6))
    bar.setPadding(dp(if (full) 22 else 12), dp(6), dp(if (full) 22 else 10), dp(6))
    val top = View(this)
    top.setBackgroundColor(Themes.withAlpha(t.cyan, 0x33))
    val wrap = FrameLayout(this)
    wrap.addView(bar, flp(MATCH, MATCH))
    wrap.addView(top, flp(MATCH, dp(1), Gravity.TOP))

    bar.addView(SpectrumView(this, music, if (full) 16 else 10), lp(dp(if (full) 110 else 54), dp(if (full) 46 else 34)).margins(r = dp(14)))
    val info = LinearLayout(this)
    info.orientation = LinearLayout.VERTICAL
    val title = Ui.oneLine(Ui.text(this, "LOADING MUSIC...", if (full) 15f else 13f, t.text, Font.DISPLAY))
    val album = Ui.oneLine(Ui.text(this, "GAME OVER  ·  INSERT COIN TO CONTINUE", 6.5f, t.magenta, Font.PIXEL))
    info.addView(title)
    info.addView(album, lp(MATCH, WRAP).margins(t = dp(4)))
    v.musicTitle = title; v.musicAlbum = album
    if (full) {
        val seekRow = LinearLayout(this)
        seekRow.orientation = LinearLayout.HORIZONTAL
        seekRow.gravity = Gravity.CENTER_VERTICAL
        val seek = neonSeek(t.cyan)
        seek.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(s: SeekBar?, p: Int, fromUser: Boolean) {}
            override fun onStartTrackingTouch(s: SeekBar?) { v.seeking = true }
            override fun onStopTrackingTouch(s: SeekBar?) { v.seeking = false; s?.let { music.seekTo(it.progress) } }
        })
        seekRow.addView(seek, lp(0, WRAP, 1f))
        val time = Ui.text(this, "0:00 / 0:00", 6.5f, t.dim, Font.PIXEL)
        seekRow.addView(time, lp(WRAP, WRAP).margins(l = dp(6)))
        info.addView(seekRow, lp(MATCH, WRAP).margins(t = dp(2)))
        v.musicSeek = seek; v.musicTime = time
    }
    bar.addView(info, lp(0, WRAP, 1f))
    fun small(glyph: String, accent: Int = t.cyan, label: String = "", click: () -> Unit) =
        Ui.button(this, label, glyph, accent, 8f) { click() }.apply { setPadding(dp(12), dp(8), dp(12), dp(8)); minimumHeight = dp(40) }
    if (full) bar.addView(small("◀◀") { music.prev() }, lp(WRAP, WRAP).margins(l = dp(8)))
    val playBtn = small("▶", t.green) { music.toggle() }
    v.musicPlay = playBtn
    bar.addView(playBtn, lp(WRAP, WRAP).margins(l = dp(6)))
    bar.addView(small("▶▶") { music.next() }, lp(WRAP, WRAP).margins(l = dp(6)))
    if (full) {
        val albumBtn = small("♫", t.magenta, "ALBUM") { albumMode = !albumMode; storyMode = false; applyFilter(); restoreFocus() }
        v.musicAlbumBtn = albumBtn
        bar.addView(albumBtn, lp(WRAP, WRAP).margins(l = dp(10)))
        val sh = small("⇄") { settings.musicShuffle = !settings.musicShuffle; refreshMusic() }
        v.musicShuffle = sh
        bar.addView(sh, lp(WRAP, WRAP).margins(l = dp(6)))
        val rp = small("↻") { settings.musicRepeat = (settings.musicRepeat + 1) % 3; refreshMusic() }
        v.musicRepeat = rp
        bar.addView(rp, lp(WRAP, WRAP).margins(l = dp(6)))
        val mute = small("♪") { settings.musicMuted = !settings.musicMuted; music.applyVolume(); refreshMusic() }
        v.musicMute = mute
        bar.addView(mute, lp(WRAP, WRAP).margins(l = dp(6)))
        val vol = neonSeek(t.violet)
        vol.max = 100
        vol.progress = (settings.musicVolume * 100).toInt()
        vol.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(s: SeekBar?, p: Int, fromUser: Boolean) { if (fromUser) { settings.musicVolume = p / 100f; settings.musicMuted = false; music.applyVolume(); refreshMusicIcons() } }
            override fun onStartTrackingTouch(s: SeekBar?) {}
            override fun onStopTrackingTouch(s: SeekBar?) {}
        })
        v.musicVolume = vol
        bar.addView(vol, lp(dp(110), WRAP).margins(l = dp(4)))
        if (gamepadConnected()) {
            bar.addView(Ui.text(this, "A SELECT  B BACK  LB RB GENRE", 6f, t.dim, Font.PIXEL), lp(WRAP, WRAP).margins(l = dp(10)))
        }
    } else {
        val albumBtn = small("♫", t.magenta) { albumMode = !albumMode; storyMode = false; applyFilter(); restoreFocus() }
        v.musicAlbumBtn = albumBtn
        bar.addView(albumBtn, lp(WRAP, WRAP).margins(l = dp(6)))
    }
    return wrap
}

private fun MainActivity.neonSeek(color: Int): SeekBar = SeekBar(this).apply {
    progressTintList = ColorStateList.valueOf(color)
    thumbTintList = ColorStateList.valueOf(color)
    progressBackgroundTintList = ColorStateList.valueOf(Themes.withAlpha(color, 0x44))
    isFocusable = true
}

fun MainActivity.gamepadConnected(): Boolean = InputDevice.getDeviceIds().any { id ->
    val d = InputDevice.getDevice(id)
    d != null && (d.sources and InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD
}

// ===================================================================== state -> views

fun MainActivity.listed(g: GameEntry) = (settings.showAll || (g.touch && g.playable) || (gamepadConnected() && g.potato && g.playable)) &&
    (settings.showTesting || g.isLive)

fun MainActivity.rebuildCatalogViews() {
    categories.clear()
    categories.add("ALL GAMES")
    categories.addAll(all.filter { listed(it) && !it.isStoryboard }.map { it.category.uppercase() }.filter { it.isNotEmpty() }.distinct().sorted())
    categoryIndex = categoryIndex.coerceIn(0, categories.size - 1)
    applyFilter()
    refreshHeader()
}

fun MainActivity.applyFilter() {
    val cat = when { albumMode -> "ALBUMS"; storyMode -> "STORYBOARDS"; categories.isEmpty() -> "ALL GAMES"; else -> categories[categoryIndex.coerceIn(0, categories.size - 1)] }
    v.categoryLabel?.text = cat
    visible.clear()
    if (albumMode) visible.addAll(music.albums.map { GameEntry.ofAlbum(it) })
    else visible.addAll(all.filter { g -> listed(g) && (if (storyMode) g.isStoryboard else !g.isStoryboard && (cat == "ALL GAMES" || g.category.equals(cat, true))) })
    v.story?.isSelected = storyMode
    v.musicAlbumBtn?.isSelected = albumMode
    v.adapter?.notifyDataSetChanged()
    val keep = visible.firstOrNull { it.id == selected?.id }
    val first = keep ?: visible.firstOrNull()
    if (first != null) {
        selectGame(first)
        v.grid?.let { g -> val i = visible.indexOf(first); g.setSelection(i) }
    } else {
        selected = null
        v.heroName?.text = if (albumMode) "LOADING ALBUMS" else if (catalogLoaded) "NO GAMES" else "SYNCING GRID..."
        v.heroMeta?.text = ""
        v.heroStatus?.visibility = View.GONE
        v.heroDesc?.text = when {
            albumMode -> "The album list is still loading. It shows up here as soon as the music is in."
            !catalogLoaded -> "Loading the game list from arcadegridz.com."
            settings.showTesting -> "No catalog entries match this filter."
            else -> "Nothing LIVE here yet. Turn on BETAS & ALPHAS to see games still in testing."
        }
        v.heroArt?.setImageDrawable(null)
        v.play?.isEnabled = false
        v.play?.setLabel("UNAVAILABLE")
        v.trailer?.visibility = View.GONE
        v.scoreList?.removeAllViews()
        v.scoreEmpty?.visibility = View.GONE
    }
}

fun MainActivity.changeCategory(delta: Int) {
    if (storyMode || albumMode) { storyMode = false; albumMode = false }
    else if (categories.isNotEmpty()) categoryIndex = (categoryIndex + delta + categories.size) % categories.size
    Sfx.play("move")
    applyFilter()
}

fun MainActivity.toggleTesting() {
    settings.showTesting = !settings.showTesting
    Sfx.play(if (settings.showTesting) "open" else "back")
    rebuildCatalogViews()
}

fun MainActivity.selectGame(g: GameEntry) {
    selected = g
    showHero(g)
    v.grid?.let { grid ->
        for (i in 0 until grid.childCount) {
            val c = grid.getChildAt(i)
            c.foreground = v.adapter?.highlight(this, c.tag == g.id)
        }
    }
}

fun MainActivity.showHero(g: GameEntry) {
    val t = Themes.current
    stopPreview()
    Images.load(g.art, v.heroArt, 1100)
    v.heroName?.text = g.name
    v.heroStatus?.let { s ->
        s.visibility = if (g.album != null) View.GONE else View.VISIBLE
        s.text = g.status.uppercase()
        s.setTextColor(Themes.statusColor(g.status))
        s.background = Ui.rounded(Color.TRANSPARENT, 6f, this, Themes.statusColor(g.status), 1f)
    }
    v.heroMeta?.text = if (g.album != null) {
        val a = g.album
        listOf(a.artist, if (a.year > 0) a.year.toString() else "", "${a.tracks.size.takeIf { it > 0 }?.toString() ?: "?"} TRACKS").filter { it.isNotEmpty() }.joinToString("  ·  ")
    } else listOf(g.genre.uppercase(), if (g.multiplayer.isNotBlank()) g.multiplayer.uppercase() else "").filter { it.isNotEmpty() }.joinToString("  ·  ")
    v.heroDesc?.text = g.description
    v.play?.let { p ->
        p.isEnabled = g.playable && (g.url != null || g.album != null)
        p.setLabel(when { g.album != null -> "PLAY ALBUM"; !g.playable -> "LOCKED"; g.isStoryboard -> "WATCH"; else -> "PLAY" })
        p.alpha = if (p.isEnabled) 1f else 0.5f
    }
    v.trailer?.visibility = if (g.trailer && g.album == null) View.VISIBLE else View.GONE
    v.scoreHeader?.setTextColor(t.gold)
    refreshScores()
    startPreviewSoon()
}

fun MainActivity.refreshScores() {
    val g = selected ?: return
    val list = v.scoreList ?: return
    list.removeAllViews()
    val t = Themes.current
    if (g.album != null) {
        v.scoreHeader?.text = "♫  TRACKS"
        val a = g.album
        v.scoreEmpty?.visibility = if (a.tracks.isEmpty()) View.VISIBLE else View.GONE
        v.scoreEmpty?.text = "LOADING TRACKS..."
        if (a.tracks.isEmpty()) bg({ Albums.loadTracks(a); a }) { r -> if (r != null && r.tracks.isNotEmpty() && selected?.id == g.id) showHero(g) }
        for ((i, tr) in a.tracks.withIndex()) {
            val row = scoreRow("%02d".format(tr.n), tr.title, fmt((tr.duration * 1000).toInt()), "", false)
            row.isFocusable = true; row.isClickable = true
            row.background = Ui.buttonBackground(this, t.magenta, 8f, ColorDrawable(Color.TRANSPARENT))
            row.setOnClickListener { Sfx.play("select"); if (music.album?.id != a.id) { music.selectAlbum(a, start = false); mainHandler.postDelayed({ music.play(i) }, 400) } else music.play(i) }
            list.addView(row)
        }
        return
    }
    v.scoreHeader?.text = "♛  HIGH SCORES"
    val scores = Leaderboards.all[g.id].orEmpty().take(5)
    v.scoreEmpty?.text = if (Leaderboards.loadedAt == 0L) "LOADING THE NETWORK BOARDS..." else "NO SCORES YET. BE THE FIRST."
    v.scoreEmpty?.visibility = if (scores.isEmpty()) View.VISIBLE else View.GONE
    val me = account?.name
    for ((i, s) in scores.withIndex()) list.addView(scoreRow("#${i + 1}", s.name, "%,d".format(s.score), s.tag, me != null && s.name.equals(me, true)))
}

fun MainActivity.scoreRow(rank: String, name: String, score: String, tag: String, mine: Boolean): LinearLayout {
    val t = Themes.current
    val row = LinearLayout(this)
    row.orientation = LinearLayout.HORIZONTAL
    row.gravity = Gravity.CENTER_VERTICAL
    row.setPadding(dp(4), dp(5), dp(4), dp(5))
    if (mine) row.background = Ui.rounded(Themes.withAlpha(t.gold, 0x33), 6f, this)
    row.addView(Ui.text(this, rank, 8.5f, t.gold, Font.PIXEL), lp(dp(42), WRAP))
    row.addView(Ui.oneLine(Ui.text(this, name, 14f, t.text, Font.BODY_BOLD)), lp(0, WRAP, 1f))
    row.addView(Ui.text(this, score, 8.5f, if (t.light) t.text else Color.WHITE, Font.PIXEL), lp(WRAP, WRAP).margins(l = dp(10), r = dp(8)))
    if (tag.isNotEmpty()) row.addView(Ui.chip(this, tag, Themes.tagColor(tag), 5.5f))
    return row
}

fun fmt(ms: Int): String { val s = (ms / 1000).coerceAtLeast(0); return "%d:%02d".format(s / 60, s % 60) }

fun MainActivity.refreshHeader() {
    val t = Themes.current
    val signedIn = token != null && account != null
    v.signIn?.visibility = if (signedIn) View.GONE else View.VISIBLE
    v.accountBtn?.visibility = if (signedIn) View.VISIBLE else View.GONE
    account?.let { a ->
        v.accountName?.text = a.name.uppercase()
        v.accountInitial?.text = a.initial
        if (a.avatar.isNotBlank()) Images.load(a.avatar, v.accountAvatar, 160) { b -> v.accountAvatar?.visibility = if (b != null) View.VISIBLE else View.GONE }
        else v.accountAvatar?.visibility = View.GONE
    }
    val count = all.count { listed(it) }
    val state = if (!catalogLoaded) "SYNCING GRID..." else if (offline) "OFFLINE CACHE" else "ONLINE  ·  $count GAMES"
    v.network?.text = if (signedIn) state else "GUEST  ·  $state"
    v.network?.setTextColor(if (offline) t.gold else t.violet)
    v.testing?.let { b ->
        b.setLabel(if (settings.showTesting) "+ BETAS & ALPHAS" else "LIVE ONLY")
        b.setGlyph(if (settings.showTesting) "◉" else "◎")
        b.isSelected = settings.showTesting
    }
    val u = update
    v.updateChip?.visibility = if (u != null) View.VISIBLE else View.GONE
    v.updateVersion?.text = if (u != null) "V${u.version}" else ""
}

fun MainActivity.refreshMusic() {
    val tr = music.track
    val a = music.album
    v.musicTitle?.text = tr?.title?.uppercase() ?: if (a == null) "LOADING MUSIC..." else a.title
    v.musicAlbum?.text = if (a != null) "${a.title}  ·  ${a.subtitle}".uppercase() else "GAME OVER  ·  INSERT COIN TO CONTINUE"
    refreshMusicIcons()
    startMusicTicker()
}

fun MainActivity.refreshMusicIcons() {
    val t = Themes.current
    v.musicPlay?.setGlyph(if (music.playing) "❚❚" else "▶")
    v.musicShuffle?.let { it.isSelected = settings.musicShuffle; it.glyphView?.setTextColor(if (settings.musicShuffle) t.green else t.cyan) }
    v.musicRepeat?.let {
        it.setGlyph(when (settings.musicRepeat) { 0 -> "→"; 2 -> "↻¹"; else -> "↻" })
        it.isSelected = settings.musicRepeat != 0
    }
    v.musicMute?.let { it.setGlyph(if (settings.musicMuted) "✕" else "♪"); it.isSelected = settings.musicMuted }
}

private var musicTicker: Runnable? = null
fun MainActivity.startMusicTicker() {
    if (musicTicker != null) return
    val r = object : Runnable {
        override fun run() {
            val d = music.duration
            v.musicSeek?.let { s -> if (!v.seeking) { s.max = maxOf(1, d); s.progress = music.position } }
            v.musicTime?.text = "${fmt(music.position)} / ${fmt(d)}"
            mainHandler.postDelayed(this, 500)
        }
    }
    musicTicker = r
    mainHandler.post(r)
}

fun MainActivity.startClock() {
    clockRun?.let { mainHandler.removeCallbacks(it) }
    val r = object : Runnable {
        override fun run() {
            v.clock?.text = DateFormat.getTimeFormat(this@startClock).format(Date())
            mainHandler.postDelayed(this, 15000)
        }
    }
    clockRun = r
    mainHandler.post(r)
}

fun MainActivity.restoreFocus() {
    if (v.detailsSheet != null) return
    val g = v.grid ?: return
    if (!g.isInTouchMode) g.requestFocus()
}

fun MainActivity.focusPlay() { v.play?.let { if (!it.isInTouchMode) it.requestFocus() } }

// ===================================================================== phone details sheet

fun MainActivity.showDetailsSheet(g: GameEntry) {
    val t = Themes.current
    val sheet = FrameLayout(this)
    sheet.setBackgroundColor(Themes.withAlpha(t.ink, 0xF2))
    sheet.isClickable = true
    val scroll = ScrollView(this)
    scroll.isVerticalScrollBarEnabled = false
    val col = LinearLayout(this)
    col.orientation = LinearLayout.VERTICAL
    col.setPadding(dp(16), dp(14), dp(16), dp(24))
    val top = LinearLayout(this)
    top.orientation = LinearLayout.HORIZONTAL
    top.gravity = Gravity.CENTER_VERTICAL
    top.addView(Ui.button(this, "BACK", "◀", t.cyan, 8f) { closePanel() }, lp(WRAP, WRAP))
    top.addView(View(this), lp(0, 1, 1f))
    top.addView(Ui.chip(this, if (g.album != null) "ALBUM" else g.category.uppercase(), t.violet, 7f))
    col.addView(top, lp(MATCH, WRAP).margins(b = dp(12)))
    buildDetails(col, heroHeightDp = if (isLandscape) 170 else 220)
    scroll.addView(col, flp(MATCH, WRAP))
    sheet.addView(scroll, flp(MATCH, MATCH))
    v.detailsSheet = sheet
    pushPanel(sheet) { v.detailsSheet = null; v.clearDetails() }
    selected = g
    showHero(g)
    focusPlay()
}

// ===================================================================== trailer preview in the details panel

fun MainActivity.startPreviewSoon() {
    previewRun?.let { mainHandler.removeCallbacks(it) }
    val g = selected ?: return
    if (settings.noTrailerPreview || !g.trailer || g.album != null || running != null) return
    if (panels.any { it.first != v.detailsSheet }) return
    val r = Runnable {
        val pv = v.previewHost ?: return@Runnable
        if (selected?.id != g.id || running != null || !pv.isShown) return@Runnable
        val p = preview ?: GameHost(this, preview = true).also { h ->
            h.view.isFocusable = false
            h.view.isFocusableInTouchMode = false
            h.view.setOnTouchListener { _, _ -> true }
            preview = h
        }
        (p.view.parent as? ViewGroup)?.removeView(p.view)
        pv.addView(p.view, flp(MATCH, MATCH))
        pv.alpha = 0f
        pv.visibility = View.VISIBLE
        p.onMessage = { type, _ -> if (type == "started") pv.animate().alpha(1f).setDuration(500).start() }
        p.quality = settings.quality
        p.launch(g, trailer = true, previewLoop = true)
        // some trailers don't announce "started": show what is there after a moment anyway
        mainHandler.postDelayed({ if (selected?.id == g.id && pv.alpha == 0f && pv.visibility == View.VISIBLE) pv.animate().alpha(1f).setDuration(500).start() }, 4000)
    }
    previewRun = r
    mainHandler.postDelayed(r, 1400)
}

fun MainActivity.stopPreview() {
    previewRun?.let { mainHandler.removeCallbacks(it) }
    previewRun = null
    preview?.let { p ->
        p.blank()
        (p.view.parent as? ViewGroup)?.removeView(p.view)
    }
    v.previewHost?.visibility = View.GONE
}

// ===================================================================== banners

fun MainActivity.loadBanners() {
    bg({ Banners.load() }) { r ->
        if (r == null || r.first.isEmpty()) return@bg
        banners = r.first
        bannerSeconds = r.second
        v.bannerFrame?.visibility = View.VISIBLE
        bannerIndex = (Math.random() * banners.size).toInt()
        Images.load(banners[bannerIndex].image, v.bannerA, 700)
        bannerRun?.let { mainHandler.removeCallbacks(it) }
        val run = object : Runnable {
            override fun run() {
                val a = v.bannerA; val b = v.bannerB
                if (a != null && b != null && banners.size > 1 && running == null) {
                    bannerIndex = (bannerIndex + 1) % banners.size
                    val front = if (a.alpha >= 0.5f) a else b
                    val back = if (front === a) b else a
                    Images.load(banners[bannerIndex].image, back, 700) {
                        back.animate().alpha(1f).setDuration(700).start()
                        front.animate().alpha(0f).setDuration(700).start()
                    }
                }
                mainHandler.postDelayed(this, bannerSeconds * 1000L)
            }
        }
        bannerRun = run
        mainHandler.postDelayed(run, bannerSeconds * 1000L)
    }
}

fun MainActivity.bannerTapped() {
    val b = banners.getOrNull(bannerIndex) ?: return
    val g = b.game?.let { id -> all.firstOrNull { it.id == id } }
    if (g != null) {
        if (!listed(g) && !g.isLive && !settings.showTesting) { settings.showTesting = true; rebuildCatalogViews() }
        storyMode = g.isStoryboard; albumMode = false
        categoryIndex = 0
        applyFilter()
        selectGame(g)
        v.grid?.setSelection(visible.indexOfFirst { it.id == g.id }.coerceAtLeast(0))
        focusPlay()
    } else if (b.url.isNotBlank()) openBrowser(b.url)
}
