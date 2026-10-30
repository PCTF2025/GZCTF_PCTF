package top.pctf.mcflag;

import org.bukkit.ChatColor;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.entity.Player;
import org.bukkit.plugin.java.JavaPlugin;

import java.io.File;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 共用服务器动态 flag 插件。
 *
 * 玩家用自己队伍的 Token 执行 /bind，插件向 GZCTF 内部接口兑换该队伍专属的 flag，
 * 之后用 /flag 查看。每队 flag 不同，把别队的 flag 交上去会被平台判定为作弊。
 */
public class FlagPlugin extends JavaPlugin {

    private static final Pattern FLAG_PATTERN = Pattern.compile("\"flag\"\\s*:\\s*\"([^\"]+)\"");

    /** 玩家 UUID -> 队伍 Token */
    private final Map<UUID, String> bindings = new HashMap<>();
    /** 玩家 UUID -> 已兑换的 flag */
    private final Map<UUID, String> flags = new HashMap<>();

    private String apiUrl;
    private String apiKey;
    private int gameId;
    private int challengeId;

    @Override
    public void onEnable() {
        saveDefaultConfig();
        apiUrl = getConfig().getString("api-url", "http://127.0.0.1:8080/api/internal/flag");
        apiKey = getConfig().getString("api-key", "");
        gameId = getConfig().getInt("game-id", 1);
        challengeId = getConfig().getInt("challenge-id", 0);

        loadBindings();
        getLogger().info("MCFlag 已启用：game=" + gameId + " challenge=" + challengeId + " api=" + apiUrl);
    }

    @Override
    public void onDisable() {
        saveBindings();
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        if (!(sender instanceof Player player)) {
            sender.sendMessage("该命令只能由玩家执行");
            return true;
        }

        switch (command.getName().toLowerCase()) {
            case "bind" -> handleBind(player, args);
            case "flag" -> handleFlag(player);
            default -> {
                return false;
            }
        }
        return true;
    }

    private void handleBind(Player player, String[] args) {
        if (args.length != 1) {
            player.sendMessage(ChatColor.YELLOW + "用法：/bind <队伍Token>（在比赛页面队伍信息处复制）");
            return;
        }

        final String token = args[0].trim();
        final UUID uuid = player.getUniqueId();

        player.sendMessage(ChatColor.GRAY + "正在兑换你队伍的 flag …");

        // HTTP 请求放到异步线程，避免阻塞主线程
        getServer().getScheduler().runTaskAsynchronously(this, () -> {
            try {
                String flag = requestFlag(token);

                getServer().getScheduler().runTask(this, () -> {
                    if (flag == null) {
                        player.sendMessage(ChatColor.RED + "兑换失败：Token 无效，或该题目未开启外部动态 flag");
                        return;
                    }

                    bindings.put(uuid, token);
                    flags.put(uuid, flag);
                    saveBindings();

                    player.sendMessage(ChatColor.GREEN + "绑定成功，你的 flag：");
                    player.sendMessage(ChatColor.AQUA + flag);
                    player.sendMessage(ChatColor.GRAY + "（可用 /flag 再次查看）");
                });
            } catch (Exception e) {
                getLogger().warning("兑换 flag 失败: " + e.getMessage());
                getServer().getScheduler().runTask(this,
                        () -> player.sendMessage(ChatColor.RED + "兑换失败，请稍后再试或联系管理员"));
            }
        });
    }

    private void handleFlag(Player player) {
        String flag = flags.get(player.getUniqueId());

        if (flag == null) {
            player.sendMessage(ChatColor.YELLOW + "你还没有绑定队伍，请先执行 /bind <队伍Token>");
            return;
        }

        player.sendMessage(ChatColor.GREEN + "你的 flag：");
        player.sendMessage(ChatColor.AQUA + flag);
    }

    /**
     * 调用 GZCTF 内部接口兑换 flag
     */
    private String requestFlag(String token) throws Exception {
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

        int code = conn.getResponseCode();

        if (code != 200)
            return null;

        String response = new String(conn.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
        Matcher matcher = FLAG_PATTERN.matcher(response);

        return matcher.find() ? matcher.group(1) : null;
    }

    private void loadBindings() {
        File file = new File(getDataFolder(), "bindings.yml");

        if (!file.exists())
            return;

        YamlConfiguration yaml = YamlConfiguration.loadConfiguration(file);

        for (String key : yaml.getKeys(false)) {
            try {
                UUID uuid = UUID.fromString(key);
                bindings.put(uuid, yaml.getString(key + ".token", ""));
                String flag = yaml.getString(key + ".flag");
                if (flag != null && !flag.isEmpty())
                    flags.put(uuid, flag);
            } catch (IllegalArgumentException ignored) {
                // 忽略损坏条目
            }
        }
    }

    private void saveBindings() {
        YamlConfiguration yaml = new YamlConfiguration();

        bindings.forEach((uuid, token) -> {
            yaml.set(uuid + ".token", token);
            String flag = flags.get(uuid);
            if (flag != null)
                yaml.set(uuid + ".flag", flag);
        });

        try {
            File file = new File(getDataFolder(), "bindings.yml");
            if (!file.getParentFile().exists() && !file.getParentFile().mkdirs())
                getLogger().warning("无法创建插件数据目录");
            yaml.save(file);
        } catch (Exception e) {
            getLogger().warning("保存绑定失败: " + e.getMessage());
        }
    }
}
