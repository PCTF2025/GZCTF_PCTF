package top.pctf.mcflag;

import org.bukkit.ChatColor;
import org.bukkit.Location;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.configuration.ConfigurationSection;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.block.BlockBreakEvent;
import org.bukkit.event.block.BlockPlaceEvent;
import org.bukkit.event.entity.EntityDamageEvent;
import org.bukkit.event.player.*;
import org.bukkit.plugin.java.JavaPlugin;
import org.bukkit.scheduler.BukkitRunnable;

import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.PBEKeySpec;
import java.io.File;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.util.*;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * PCTF 服务器插件：登录 + 领取码换 flag + 资源包开关
 *
 * 玩家流程：
 *   /register <密码> <密码> <队伍Token>   注册（必须绑定队伍 Token）
 *   /login <密码>                        登录（每次进服都要）
 *   /claim <领取码>                       换取自己队伍的 flag
 *   /flag                                查看已领取的 flag
 *
 * 未登录时不能移动、聊天、破坏/放置方块、受伤。
 */
public class FlagPlugin extends JavaPlugin implements Listener {

    private static final Pattern FLAG_PATTERN = Pattern.compile("\"flag\"\\s*:\\s*\"([^\"]+)\"");
    private static final int MAX_ATTEMPTS = 5;
    private static final int LOGIN_TIMEOUT_SEC = 90;

    /** 小写玩家名 -> 账号 */
    private static final class Account {
        String name;
        String salt;
        String hash;
        String teamToken;
        Map<String, String> claims = new LinkedHashMap<>();
    }

    private final Map<String, Account> accounts = new HashMap<>();
    private final Set<UUID> online = new HashSet<>();
    private final Map<UUID, Integer> attempts = new HashMap<>();
    private final Map<UUID, Location> frozen = new HashMap<>();
    /** 进服时间，用于「每个玩家」的登录超时 */
    private final Map<UUID, Long> joinedAt = new HashMap<>();
    private final Map<String, Integer> codes = new LinkedHashMap<>();

    private String apiUrl;
    private String apiKey;
    private int gameId;

    // 门卫：站在指定点附近、主手拿着名字内涵 ops 的物品即可开闸
    // 外侧压力板用于进、内侧压力板用于出；开门后只要有人跨过闸门就立即关闭，
    // 因此一次只放一个人通过。关闭时恢复原始方块（铁栏杆），不会变成基岩。
    private boolean gateEnabled;
    private org.bukkit.Location gateOuter;
    private org.bukkit.Location gateInner;
    private double gateRadius;
    private boolean gateOpened;
    /** 本次开门是从外侧（进）还是内侧（出）触发的 */
    private boolean gateOpenedFromOuter;
    private long gateOpenedAt;
    /** 兜底：压力板被踩住不放时，最多开这么久 */
    private long gateMaxOpenMs;
    /** 门卫室内部区域，用来判断「是否已跨过闸门」 */
    private org.bukkit.util.BoundingBox gateInterior;
    /** 本次开门的玩家，只有他跨过闸门才关门（避免无关玩家误触发） */
    private java.util.UUID gateOpener;
    /** 压力板「已武装」：踩过一次后必须离开才能再次开，防止站着不动反复开关 */
    private boolean outerArmed = true;
    private boolean innerArmed = true;
    private final java.util.List<org.bukkit.Location> gateBlocks = new java.util.ArrayList<>();
    /** 闸门各格开门前的原始方块，关门时原样恢复 */
    private final java.util.Map<org.bukkit.Location, org.bukkit.Material> gateOriginal = new java.util.HashMap<>();

    private boolean packEnabled;
    private String packUrl;
    private String packSha1;
    private boolean packRequired;

    private final SecureRandom random = new SecureRandom();

    @Override
    public void onEnable() {
        saveDefaultConfig();
        apiUrl = getConfig().getString("api-url", "http://127.0.0.1:8080/api/internal/flag");
        apiKey = getConfig().getString("api-key", "");
        gameId = getConfig().getInt("game-id", 1);

        loadCodes();
        loadAccounts();
        loadPackConfig();
        loadGateConfig();
        getServer().getPluginManager().registerEvents(this, this);
        startAuthTicker();

        getLogger().info("PCTF 插件已启用：game=" + gameId
                + " 账号 " + accounts.size() + " 个，领取码 " + codes.size() + " 个");
        getLogger().info("强制资源包：" + (packEnabled ? "开启 -> " + packUrl : "关闭"));
    }

    @Override
    public void onDisable() {
        saveAccounts();
    }

    // ==================== 命令 ====================

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        String name = command.getName().toLowerCase();

        if (name.equals("mcpack")) {
            handlePack(sender, args);
            return true;
        }

        if (!(sender instanceof Player player)) {
            sender.sendMessage("该命令只能由玩家执行");
            return true;
        }

        switch (name) {
            case "register" -> handleRegister(player, args);
            case "login" -> handleLogin(player, args);
            case "bind" -> handleBind(player, args);
            case "claim" -> handleClaim(player, args);
            case "flag" -> handleFlag(player);
            default -> {
                return false;
            }
        }
        return true;
    }

    private void handleRegister(Player player, String[] args) {
        String key = player.getName().toLowerCase();

        if (accounts.containsKey(key)) {
            player.sendMessage(ChatColor.YELLOW + "该账号已注册，请用 /login <密码>");
            return;
        }

        if (args.length != 3) {
            player.sendMessage(ChatColor.YELLOW + "用法：/register <密码> <确认密码> <队伍Token>");
            player.sendMessage(ChatColor.GRAY + "队伍 Token 在比赛页面的队伍卡片上复制");
            return;
        }

        if (!args[0].equals(args[1])) {
            player.sendMessage(ChatColor.RED + "两次输入的密码不一致");
            return;
        }

        if (args[0].length() < 6) {
            player.sendMessage(ChatColor.RED + "密码至少 6 位");
            return;
        }

        final String password = args[0];
        final String token = args[2].trim();
        final UUID uuid = player.getUniqueId();

        player.sendMessage(ChatColor.GRAY + "正在校验队伍 Token …");

        getServer().getScheduler().runTaskAsynchronously(this, () -> {
            String probe = requestFlag(firstChallengeId(), token, false);
            final boolean valid = probe != null;

            getServer().getScheduler().runTask(this, () -> {
                if (!valid) {
                    player.sendMessage(ChatColor.RED + "队伍 Token 无效，注册失败");
                    return;
                }

                Account acc = new Account();
                acc.name = player.getName();
                acc.salt = randomHex(16);
                acc.hash = hash(password, acc.salt);
                acc.teamToken = token;
                accounts.put(key, acc);
                saveAccounts();

                online.add(uuid);
                frozen.remove(uuid);
                joinedAt.remove(uuid);
                player.sendMessage(ChatColor.GREEN + "注册并登录成功，队伍已绑定！");
                player.sendMessage(ChatColor.GRAY + "用 /claim <领取码> 领取各题 flag");
            });
        });
    }

    private void handleLogin(Player player, String[] args) {
        String key = player.getName().toLowerCase();
        Account acc = accounts.get(key);

        if (acc == null) {
            player.sendMessage(ChatColor.YELLOW + "你还没注册：/register <密码> <确认密码> <队伍Token>");
            return;
        }

        if (args.length != 1) {
            player.sendMessage(ChatColor.YELLOW + "用法：/login <密码>");
            return;
        }

        if (!acc.hash.equals(hash(args[0], acc.salt))) {
            int n = attempts.merge(player.getUniqueId(), 1, Integer::sum);
            player.sendMessage(ChatColor.RED + "密码错误（" + n + "/" + MAX_ATTEMPTS + "）");
            if (n >= MAX_ATTEMPTS) {
                player.kickPlayer(ChatColor.RED + "密码错误次数过多");
            }
            return;
        }

        attempts.remove(player.getUniqueId());
        online.add(player.getUniqueId());
        frozen.remove(player.getUniqueId());
        joinedAt.remove(player.getUniqueId());
        player.sendMessage(ChatColor.GREEN + "登录成功，欢迎回来 " + acc.name);
    }

    private void handleBind(Player player, String[] args) {
        if (!online.contains(player.getUniqueId())) {
            player.sendMessage(ChatColor.RED + "请先登录");
            return;
        }

        if (args.length != 1) {
            player.sendMessage(ChatColor.YELLOW + "用法：/bind <队伍Token>（换队时用）");
            return;
        }

        Account acc = accounts.get(player.getName().toLowerCase());
        if (acc == null) {
            player.sendMessage(ChatColor.RED + "账号异常");
            return;
        }

        final String token = args[0].trim();
        player.sendMessage(ChatColor.GRAY + "正在校验队伍 Token …");

        getServer().getScheduler().runTaskAsynchronously(this, () -> {
            boolean valid = requestFlag(firstChallengeId(), token, false) != null;
            getServer().getScheduler().runTask(this, () -> {
                if (!valid) {
                    player.sendMessage(ChatColor.RED + "Token 无效");
                    return;
                }
                acc.teamToken = token;
                saveAccounts();
                player.sendMessage(ChatColor.GREEN + "队伍已更新");
            });
        });
    }

    private void handleClaim(Player player, String[] args) {
        if (!online.contains(player.getUniqueId())) {
            player.sendMessage(ChatColor.RED + "请先登录");
            return;
        }

        if (args.length != 1) {
            player.sendMessage(ChatColor.YELLOW + "用法：/claim <领取码>");
            return;
        }

        Account acc = accounts.get(player.getName().toLowerCase());
        if (acc == null) {
            player.sendMessage(ChatColor.RED + "账号异常");
            return;
        }

        final String code = args[0].trim().toUpperCase();
        final Integer cid = codes.get(code);

        if (cid == null) {
            player.sendMessage(ChatColor.RED + "领取码无效");
            return;
        }

        if (acc.claims.containsKey(code)) {
            player.sendMessage(ChatColor.GREEN + "这题你已经领过了：");
            player.sendMessage(ChatColor.AQUA + acc.claims.get(code));
            return;
        }

        player.sendMessage(ChatColor.GRAY + "验证中 …");

        getServer().getScheduler().runTaskAsynchronously(this, () -> {
            String flag = requestFlag(cid, acc.teamToken, false);

            getServer().getScheduler().runTask(this, () -> {
                if (flag == null) {
                    player.sendMessage(ChatColor.RED + "领取失败，请联系管理员");
                    return;
                }
                acc.claims.put(code, flag);
                saveAccounts();
                player.sendMessage(ChatColor.GREEN + "领取成功！你的 flag：");
                player.sendMessage(ChatColor.AQUA + flag);
            });
        });
    }

    private void handleFlag(Player player) {
        if (!online.contains(player.getUniqueId())) {
            player.sendMessage(ChatColor.RED + "请先登录");
            return;
        }

        Account acc = accounts.get(player.getName().toLowerCase());
        if (acc == null || acc.claims.isEmpty()) {
            player.sendMessage(ChatColor.YELLOW + "还没有领取任何 flag，用 /claim <领取码>");
            return;
        }

        player.sendMessage(ChatColor.GREEN + "已领取的 flag：");
        acc.claims.forEach((code, flag) ->
                player.sendMessage(ChatColor.GRAY + "[" + code + "] " + ChatColor.AQUA + flag));
    }

    // ==================== 资源包 ====================

    private void handlePack(CommandSender sender, String[] args) {
        String arg = args.length > 0 ? args[0].toLowerCase() : "status";

        switch (arg) {
            case "on", "off" -> {
                packEnabled = arg.equals("on");
                getConfig().set("resource-pack.enabled", packEnabled);
                saveConfig();
                String msg = "资源包已" + (packEnabled ? "开启" : "关闭");
                sender.sendMessage(ChatColor.GREEN + msg);
                getLogger().info(msg + " url=" + packUrl);
            }
            case "status" -> {
                sender.sendMessage(ChatColor.AQUA + "资源包：" + (packEnabled ? "开启" : "关闭"));
                sender.sendMessage(ChatColor.GRAY + "url=" + packUrl + " required=" + packRequired);
            }
            default -> sender.sendMessage(ChatColor.YELLOW + "用法：/mcpack <on|off|status>");
        }
    }

    private void loadGateConfig() {
        gateEnabled = getConfig().getBoolean("gate.enabled", false);
        gateRadius = getConfig().getDouble("gate.radius", 2.0);
        gateMaxOpenMs = getConfig().getLong("gate.max-open-seconds", 15) * 1000L;
        gateOpened = false;
        gateOpenedFromOuter = false;
        gateBlocks.clear();
        gateOriginal.clear();

        String world = getConfig().getString("gate.world", "world");
        org.bukkit.World w = getServer().getWorld(world);
        if (w == null)
            return;

        gateOuter = readPoint(w, "gate.outer");
        gateInner = readPoint(w, "gate.inner");

        // 兼容旧配置（只有 gate.x/y/z）：把它当作外侧压力板
        if (gateOuter == null)
            gateOuter = new org.bukkit.Location(w,
                    getConfig().getDouble("gate.x", 0),
                    getConfig().getDouble("gate.y", 64),
                    getConfig().getDouble("gate.z", 0));

        for (String key : getConfig().getStringList("gate.blocks")) {
            String[] parts = key.split(",");
            if (parts.length != 3)
                continue;
            gateBlocks.add(new org.bukkit.Location(w,
                    Double.parseDouble(parts[0].trim()),
                    Double.parseDouble(parts[1].trim()),
                    Double.parseDouble(parts[2].trim())));
        }

        // 内部区域：缺省时按闸门位置往「门内」推 1~6 格
        String interior = getConfig().getString("gate.interior", "");
        if (interior != null && !interior.isBlank()) {
            double[] v = parseSix(interior);
            if (v != null)
                gateInterior = new org.bukkit.util.BoundingBox(v[0], v[1], v[2], v[3], v[4], v[5]);
        }

        getLogger().info("门卫：" + (gateEnabled
                ? "启用  外侧 " + fmt(gateOuter) + "  内侧 " + fmt(gateInner)
                  + "  闸门 " + gateBlocks.size() + " 格"
                  + (gateInterior == null ? "（未配置内部区域，跨门判定关闭）" : "")
                : "关闭"));
    }

    private org.bukkit.Location readPoint(org.bukkit.World w, String path) {
        if (!getConfig().isConfigurationSection(path))
            return null;
        return new org.bukkit.Location(w,
                getConfig().getDouble(path + ".x"),
                getConfig().getDouble(path + ".y"),
                getConfig().getDouble(path + ".z"));
    }

    private static double[] parseSix(String s) {
        String[] parts = s.split(",");
        if (parts.length != 6)
            return null;
        double[] v = new double[6];
        try {
            for (int i = 0; i < 6; i++)
                v[i] = Double.parseDouble(parts[i].trim());
        } catch (NumberFormatException e) {
            return null;
        }
        return v;
    }

    private static String fmt(org.bukkit.Location l) {
        return l == null ? "(未配置)"
                : l.getBlockX() + "," + l.getBlockY() + "," + l.getBlockZ();
    }

    // ==================== 门卫逻辑 ====================

    private org.bukkit.World getWorldOfGate() {
        if (gateOuter != null)
            return gateOuter.getWorld();
        if (gateInner != null)
            return gateInner.getWorld();
        return gateBlocks.isEmpty() ? null : gateBlocks.get(0).getWorld();
    }

    /** 主手物品的显示名是否包含 ops */
    private boolean holdsOpsBadge(Player p) {
        org.bukkit.inventory.ItemStack hand = p.getInventory().getItemInMainHand();
        if (hand == null || hand.getType() == org.bukkit.Material.AIR)
            return false;
        org.bukkit.inventory.meta.ItemMeta meta = hand.getItemMeta();
        return meta != null && meta.getDisplayName() != null && meta.getDisplayName().contains("ops");
    }

    /**
     * 开门者本人是否已经跨过闸门。
     * 只认开门者，其他玩家走过来不会误触发，从而保证「一次只放一个人」。
     * 从外侧开的：等他真正进到门内（z 越过闸门）才关门；
     * 从内侧开的：等他真正走到门外才关门。
     */
    private boolean openerAcross() {
        if (gateOpener == null)
            return false;
        Player p = getServer().getPlayer(gateOpener);
        if (p == null || p.getWorld() != getWorldOfGate())
            return false;

        // 只按内部区域判定：外侧开的等他进到门内，内侧开的等他走到门外。
        // 内部区域上界要含住内侧压力板所在位置，否则站在板上就会被误判为「已跨出」。
        boolean inside = gateInterior.contains(p.getLocation().toVector());
        return gateOpenedFromOuter ? inside : !inside;
    }

    private void openGate(Player p, boolean fromOuter) {
        gateOpened = true;
        gateOpener = p.getUniqueId();
        gateOpenedFromOuter = fromOuter;
        gateOpenedAt = System.currentTimeMillis();

        gateOriginal.clear();
        for (org.bukkit.Location loc : gateBlocks) {
            org.bukkit.block.Block b = loc.getBlock();
            gateOriginal.put(loc, b.getType());   // 记住原始方块（铁栏杆）
            b.setType(org.bukkit.Material.AIR);
        }

        broadcast("§a[门卫] 工牌通过，闸门开了——只放一个人过。");
        getLogger().info("门卫开闸：" + p.getName() + " 持 " + p.getInventory().getItemInMainHand()
                .getItemMeta().getDisplayName() + (fromOuter ? "（从外侧进）" : "（从内侧出）"));
    }

    /** 关门：恢复原始方块，绝不替换成基岩 */
    private void closeGate(String reason) {
        for (java.util.Map.Entry<org.bukkit.Location, org.bukkit.Material> e : gateOriginal.entrySet())
            e.getKey().getBlock().setType(e.getValue());
        gateOriginal.clear();

        gateOpened = false;
        gateOpenedFromOuter = false;
        gateOpener = null;
        broadcast("§7[门卫] 闸门已关闭" + (reason == null ? "" : "（" + reason + "）") + "。");
    }

    private void broadcast(String msg) {
        getServer().broadcastMessage(msg);
    }

    private void loadPackConfig() {
        packEnabled = getConfig().getBoolean("resource-pack.enabled", false);
        packUrl = getConfig().getString("resource-pack.url", "");
        packSha1 = getConfig().getString("resource-pack.sha1", "");
        packRequired = getConfig().getBoolean("resource-pack.required", true);
    }

    @EventHandler
    public void onJoin(PlayerJoinEvent event) {
        Player player = event.getPlayer();
        UUID uuid = player.getUniqueId();

        if (packEnabled && packUrl != null && !packUrl.isEmpty())
            player.setResourcePack(packUrl, sha1Bytes(packSha1), packRequired);

        if (accounts.containsKey(player.getName().toLowerCase())) {
            player.sendMessage(ChatColor.YELLOW + "请登录：/login <密码>");
        } else {
            player.sendMessage(ChatColor.YELLOW + "请注册：/register <密码> <确认密码> <队伍Token>");
            player.sendMessage(ChatColor.GRAY + "队伍 Token 在比赛页面队伍卡片上复制");
        }

        frozen.put(uuid, player.getLocation());
        joinedAt.put(uuid, System.currentTimeMillis());
    }

    /** 未登录玩家每 2 秒检查一次：拉回原位 + 超时踢出 */
    private void startAuthTicker() {
        new BukkitRunnable() {
            @Override
            public void run() {
                for (Player p : getServer().getOnlinePlayers()) {
                    UUID id = p.getUniqueId();
                    if (online.contains(id)) {
                        frozen.remove(id);
                        continue;
                    }

                    Location lock = frozen.get(id);
                    if (lock != null && p.getLocation().distanceSquared(lock) > 4)
                        p.teleport(lock);
                }
            }
        }.runTaskTimer(this, 40L, 40L);

        // 门卫：站在压力板附近 + 主手拿着名字含 ops 的物品 → 开闸
        // 开门后一旦有人跨过闸门（外侧开的则有人进入，内侧开的则有人出去）立即关门，
        // 所以一次只放一个人通过；超出 max-open-seconds 也会自动关，避免被踩住不放。
        new BukkitRunnable() {
            @Override
            public void run() {
                if (!gateEnabled)
                    return;

                if (gateOpened) {
                    boolean timeout = System.currentTimeMillis() - gateOpenedAt > gateMaxOpenMs;
                    boolean crossed = gateInterior != null && openerAcross();
                    if (timeout || crossed)
                        closeGate(crossed ? "有人通过" : "超时");
                    return;
                }

                if (gateOuter == null && gateInner == null)
                    return;

                for (Player p : getServer().getOnlinePlayers()) {
                    if (!online.contains(p.getUniqueId()))
                        continue;
                    if (p.getWorld() != getWorldOfGate())
                        continue;

                    boolean atOuter = gateOuter != null && p.getLocation().distance(gateOuter) <= gateRadius;
                    boolean atInner = gateInner != null && p.getLocation().distance(gateInner) <= gateRadius;

                    // 已经踩着的板需要「先离开」才能再次触发，否则站着不动会一次一次重开
                    if (!atOuter)
                        outerArmed = true;
                    if (!atInner)
                        innerArmed = true;

                    if (!holdsOpsBadge(p))
                        continue;
                    if (atOuter && outerArmed) {
                        outerArmed = false;
                        openGate(p, true);
                        break;
                    }
                    if (atInner && innerArmed) {
                        innerArmed = false;
                        openGate(p, false);
                        break;
                    }
                }
            }
        }.runTaskTimer(this, 40L, 2L);

        // 每 5 秒扫一次，只踢「自己进服超过 90 秒仍未登录」的玩家
        new BukkitRunnable() {
            @Override
            public void run() {
                long now = System.currentTimeMillis();
                for (Player p : getServer().getOnlinePlayers()) {
                    UUID id = p.getUniqueId();
                    if (online.contains(id))
                        continue;
                    Long t = joinedAt.get(id);
                    if (t != null && now - t > LOGIN_TIMEOUT_SEC * 1000L) {
                        p.kickPlayer(ChatColor.RED + "登录超时（" + LOGIN_TIMEOUT_SEC + " 秒），请重新进服并 /login");
                    }
                }
            }
        }.runTaskTimer(this, 100L, 100L);
    }

    // ---- 未登录限制 ----

    @EventHandler(priority = EventPriority.LOWEST)
    public void onChat(AsyncPlayerChatEvent event) {
        if (!online.contains(event.getPlayer().getUniqueId())) {
            event.setCancelled(true);
            event.getPlayer().sendMessage(ChatColor.RED + "请先登录");
        }
    }

    @EventHandler(priority = EventPriority.LOWEST)
    public void onMove(PlayerMoveEvent event) {
        if (!online.contains(event.getPlayer().getUniqueId()))
            event.setTo(event.getFrom());
    }

    @EventHandler(priority = EventPriority.LOWEST)
    public void onBreak(BlockBreakEvent event) {
        if (!online.contains(event.getPlayer().getUniqueId()))
            event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.LOWEST)
    public void onPlace(BlockPlaceEvent event) {
        if (!online.contains(event.getPlayer().getUniqueId()))
            event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.LOWEST)
    public void onInteract(PlayerInteractEvent event) {
        if (!online.contains(event.getPlayer().getUniqueId()) && event.getClickedBlock() != null)
            event.setCancelled(true);
    }

    @EventHandler(priority = EventPriority.LOWEST)
    public void onDamage(EntityDamageEvent event) {
        if (event.getEntity() instanceof Player p && !online.contains(p.getUniqueId()))
            event.setCancelled(true);
    }

    @EventHandler
    public void onQuit(PlayerQuitEvent event) {
        UUID id = event.getPlayer().getUniqueId();
        online.remove(id);
        frozen.remove(id);
        attempts.remove(id);
        joinedAt.remove(id);
    }

    // ==================== 平台接口 ====================

    private int firstChallengeId() {
        return codes.isEmpty() ? 0 : codes.values().iterator().next();
    }

    private String requestFlag(int challengeId, String token, boolean quiet) throws RuntimeException {
        try {
            String body = String.format("{\"gameId\":%d,\"challengeId\":%d,\"teamToken\":\"%s\"}",
                    gameId, challengeId, token.replace("\"", ""));

            HttpURLConnection conn = (HttpURLConnection) new URL(apiUrl).openConnection();
            conn.setRequestMethod("POST");
            conn.setConnectTimeout(5000);
            conn.setReadTimeout(5000);
            conn.setDoOutput(true);
            conn.setRequestProperty("Content-Type", "application/json");
            conn.setRequestProperty("X-Internal-Key", apiKey);

            try (OutputStream os = conn.getOutputStream()) {
                os.write(body.getBytes(StandardCharsets.UTF_8));
            }

            if (conn.getResponseCode() != 200)
                return null;

            String resp = new String(conn.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
            Matcher m = FLAG_PATTERN.matcher(resp);
            return m.find() ? m.group(1) : null;
        } catch (Exception e) {
            getLogger().warning("请求平台失败: " + e.getMessage());
            return null;
        }
    }

    // ==================== 工具 ====================

    private static String hash(String password, String salt) {
        try {
            PBEKeySpec spec = new PBEKeySpec(password.toCharArray(), salt.getBytes(StandardCharsets.UTF_8),
                    50000, 256);
            byte[] raw = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(spec).getEncoded();
            return Base64.getEncoder().encodeToString(raw);
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    private String randomHex(int bytes) {
        byte[] b = new byte[bytes];
        random.nextBytes(b);
        StringBuilder sb = new StringBuilder();
        for (byte x : b) sb.append(String.format("%02x", x));
        return sb.toString();
    }

    private static byte[] sha1Bytes(String hex) {
        if (hex == null || hex.length() != 40) return null;
        byte[] out = new byte[20];
        for (int i = 0; i < 20; i++)
            out[i] = (byte) Integer.parseInt(hex.substring(i * 2, i * 2 + 2), 16);
        return out;
    }

    private void loadCodes() {
        codes.clear();
        ConfigurationSection sec = getConfig().getConfigurationSection("codes");
        if (sec == null) return;
        for (String key : sec.getKeys(false)) {
            int cid = sec.getInt(key, 0);
            if (cid > 0) codes.put(key.toUpperCase(), cid);
        }
    }

    private void loadAccounts() {
        File file = new File(getDataFolder(), "accounts.yml");
        if (!file.exists()) return;

        YamlConfiguration yaml = YamlConfiguration.loadConfiguration(file);
        for (String key : yaml.getKeys(false)) {
            Account acc = new Account();
            acc.name = yaml.getString(key + ".name", key);
            acc.salt = yaml.getString(key + ".salt", "");
            acc.hash = yaml.getString(key + ".hash", "");
            acc.teamToken = yaml.getString(key + ".token", "");

            ConfigurationSection cs = yaml.getConfigurationSection(key + ".claims");
            if (cs != null)
                for (String c : cs.getKeys(false))
                    acc.claims.put(c, cs.getString(c, ""));

            accounts.put(key, acc);
        }
    }

    private void saveAccounts() {
        YamlConfiguration yaml = new YamlConfiguration();
        accounts.forEach((key, acc) -> {
            yaml.set(key + ".name", acc.name);
            yaml.set(key + ".salt", acc.salt);
            yaml.set(key + ".hash", acc.hash);
            yaml.set(key + ".token", acc.teamToken);
            acc.claims.forEach((c, f) -> yaml.set(key + ".claims." + c, f));
        });

        try {
            File file = new File(getDataFolder(), "accounts.yml");
            if (!file.getParentFile().exists() && !file.getParentFile().mkdirs())
                getLogger().warning("无法创建插件目录");
            yaml.save(file);
        } catch (Exception e) {
            getLogger().warning("保存账号失败: " + e.getMessage());
        }
    }
}
