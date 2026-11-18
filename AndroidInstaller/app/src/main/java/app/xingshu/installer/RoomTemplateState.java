package app.xingshu.installer;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.Random;

/**
 * Pure local preview state for the mobile "趴开房" & room/squad template.
 * Keeps preview state isolated from live network APIs so unit tests stay deterministic.
 */
public final class RoomTemplateState {
    public static final String ZERO_YIELD_NOTICE =
        "0 收益 · 自托管托管模式（娱乐演练：不结算官方经验、星币、角色好感或任务进度）";

    public static final String[] ROOM_TAGS = { "休闲", "速刷", "冲刺", "教学", "娱乐" };

    public enum HostTarget {
        OFFICIAL("官方现有模式"),
        SELF_HOSTED("自托管服务器 · 0收益");

        public final String label;

        HostTarget(String label) {
            this.label = label;
        }
    }

    public enum Mode {
        PVE("PVE 合作", "PVE", true),
        PVP("PVP 对战", "PVP", true),
        COOP_EVENT("合作活动 · 0收益", "活动", false),
        SELF_HOSTED("星枢自托管 · 0收益", "自托管", false);

        public final String label;
        public final String commandToken;
        public final boolean officialAvailable;

        Mode(String label, String commandToken, boolean officialAvailable) {
            this.label = label;
            this.commandToken = commandToken;
            this.officialAvailable = officialAvailable;
        }
    }

    public enum ThinkTime {
        FAST("快"),
        MEDIUM("中"),
        SLOW("慢");

        public final String label;

        ThinkTime(String label) {
            this.label = label;
        }
    }

    public enum Privacy {
        PUBLIC("公开房", "公开"),
        FRIENDS("仅好友", "仅好友"),
        PASSWORD("密码房", "密码");

        public final String label;
        public final String commandToken;

        Privacy(String label, String commandToken) {
            this.label = label;
            this.commandToken = commandToken;
        }
    }

    public enum PreviewStatus {
        IDLE("空闲待机"),
        RECRUITING("招募中"),
        READY_FULL("满员备战中");

        public final String label;

        PreviewStatus(String label) {
            this.label = label;
        }
    }

    public static final class MapPreset {
        public final int id;
        public final String name;
        public final Mode mode;
        public final List<String> difficulties;

        MapPreset(int id, String name, Mode mode, String... diffs) {
            this.id = id;
            this.name = name;
            this.mode = mode;
            this.difficulties = diffs.length == 0
                ? Collections.emptyList()
                : Collections.unmodifiableList(Arrays.asList(diffs));
        }
    }

    public static final class SquadSlot {
        public final int seat;
        public boolean occupied;
        public boolean host;
        public String playerName;
        public String uid;
        public String characterName;
        public boolean ready;

        SquadSlot(int seat) {
            this.seat = seat;
        }
    }

    public static final List<MapPreset> MAPS = Collections.unmodifiableList(Arrays.asList(
        new MapPreset(82007, "星趴·梦想号", Mode.PVE, "普通", "困难", "噩梦", "疯狂", "极限"),
        new MapPreset(82008, "御魂庆典", Mode.PVE, "普通", "困难", "噩梦", "疯狂", "极限"),
        new MapPreset(82010, "水乡古镇", Mode.PVE, "普通", "困难", "噩梦", "疯狂", "极限"),
        new MapPreset(82013, "龙宫游乐园", Mode.PVE, "普通", "困难", "噩梦", "疯狂"),
        new MapPreset(82016, "异变图书馆", Mode.PVE, "普通", "困难", "噩梦", "疯狂"),
        new MapPreset(82017, "森秋幻境", Mode.PVE, "普通", "困难", "噩梦", "疯狂"),
        new MapPreset(81003, "游乐园", Mode.PVP),
        new MapPreset(81005, "樱花小镇", Mode.PVP),
        new MapPreset(81006, "魅影都市", Mode.PVP),
        new MapPreset(81011, "丛林冒险", Mode.PVP),
        new MapPreset(82009, "礼物广场", Mode.COOP_EVENT),
        new MapPreset(82012, "魔法学院", Mode.COOP_EVENT),
        new MapPreset(83003, "决赛大赛场", Mode.COOP_EVENT),
        new MapPreset(99001, "星枢自定义沙盒场", Mode.SELF_HOSTED, "普通", "困难", "噩梦", "疯狂", "极限"),
        new MapPreset(99002, "绝版活动复刻演练", Mode.SELF_HOSTED, "普通", "困难", "噩梦", "疯狂")
    ));

    private static final String[] SAMPLE_HEROES = { "帕露南", "姬梦枫", "米米", "恋", "南希露", "美咲" };

    public HostTarget draftHostTarget = HostTarget.OFFICIAL;
    public Mode draftMode = Mode.PVE;
    public MapPreset draftMap = MAPS.get(0);
    public String draftDifficulty = "噩梦";
    public ThinkTime draftThinkTime = ThinkTime.MEDIUM;
    public String draftRoomTag = "速刷";
    public int draftMaxPlayers = 4;
    public Privacy draftPrivacy = Privacy.PASSWORD;
    public String draftPassword = "2580";
    public String draftRoomName = "日常刷本速车 · 来不抢输出";

    public String roomCode = "849206";
    public HostTarget activeHostTarget = HostTarget.OFFICIAL;
    public Mode activeMode = Mode.PVE;
    public MapPreset activeMap = MAPS.get(0);
    public String activeDifficulty = "噩梦";
    public ThinkTime activeThinkTime = ThinkTime.MEDIUM;
    public String activeRoomTag = "速刷";
    public int activeMaxPlayers = 4;
    public Privacy activePrivacy = Privacy.PASSWORD;
    public String activePassword = "2580";
    public String activeRoomName = "日常刷本速车 · 来不抢输出";
    public PreviewStatus status = PreviewStatus.RECRUITING;
    public final List<SquadSlot> slots = new ArrayList<>(4);
    private final Random random = new Random();

    public RoomTemplateState() {
        for (int i = 1; i <= 4; i++) {
            slots.add(new SquadSlot(i));
        }
        applyStatusPreset(PreviewStatus.RECRUITING);
    }

    public List<MapPreset> mapsForDraftMode() {
        List<MapPreset> out = new ArrayList<>();
        for (MapPreset m : MAPS) {
            if (m.mode == draftMode) out.add(m);
        }
        return out;
    }

    public void setDraftHostTarget(HostTarget target) {
        this.draftHostTarget = target;
        if (target == HostTarget.OFFICIAL && !draftMode.officialAvailable) {
            setDraftMode(Mode.PVE);
        } else if (target == HostTarget.SELF_HOSTED && draftMode.officialAvailable) {
            setDraftMode(Mode.COOP_EVENT);
        }
    }

    public void setDraftMode(Mode mode) {
        this.draftMode = mode;
        if (!mode.officialAvailable) {
            this.draftHostTarget = HostTarget.SELF_HOSTED;
        }
        List<MapPreset> list = mapsForDraftMode();
        if (!list.isEmpty()) {
            setDraftMap(list.get(0));
        }
    }

    public void setDraftMap(MapPreset map) {
        this.draftMap = map;
        if (map.difficulties.isEmpty()) {
            this.draftDifficulty = "";
        } else if (!map.difficulties.contains(this.draftDifficulty)) {
            this.draftDifficulty = map.difficulties.get(Math.min(2, map.difficulties.size() - 1));
        }
    }

    public boolean isDraftZeroYield() {
        return draftHostTarget == HostTarget.SELF_HOSTED || !draftMode.officialAvailable;
    }

    public boolean isActiveZeroYield() {
        return activeHostTarget == HostTarget.SELF_HOSTED || !activeMode.officialAvailable;
    }

    public static String sanitizeNumericPassword(String raw) {
        if (raw == null) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < raw.length() && sb.length() < 6; i++) {
            char c = raw.charAt(i);
            if (c >= '0' && c <= '9') {
                sb.append(c);
            }
        }
        return sb.toString();
    }

    public Privacy effectiveDraftPrivacy() {
        if (draftPrivacy == Privacy.PASSWORD && sanitizeNumericPassword(draftPassword).isEmpty()) {
            return Privacy.PUBLIC;
        }
        return draftPrivacy;
    }

    public Privacy effectiveActivePrivacy() {
        if (activePrivacy == Privacy.PASSWORD && sanitizeNumericPassword(activePassword).isEmpty()) {
            return Privacy.PUBLIC;
        }
        return activePrivacy;
    }

    public String buildPaKaiFangCommand() {
        StringBuilder sb = new StringBuilder();
        sb.append("趴开房 ").append(draftMode.commandToken).append(' ').append(draftMap.name);
        if (!draftDifficulty.isEmpty() && !draftMap.difficulties.isEmpty()) {
            sb.append(' ').append(draftDifficulty);
        }
        sb.append(" 思考:").append(draftThinkTime.label);
        if (draftRoomTag != null && !draftRoomTag.trim().isEmpty()) {
            sb.append(" 标签:").append(draftRoomTag.trim());
        }
        sb.append(' ').append(draftMaxPlayers).append("人");
        Privacy effPrivacy = effectiveDraftPrivacy();
        if (effPrivacy == Privacy.PASSWORD) {
            sb.append(" 密码:").append(sanitizeNumericPassword(draftPassword));
        } else {
            sb.append(' ').append(effPrivacy.commandToken);
        }
        if (isDraftZeroYield()) {
            sb.append(" 自托管0收益");
        }
        if (draftRoomName != null && !draftRoomName.trim().isEmpty()) {
            sb.append(' ').append(draftRoomName.trim());
        }
        return sb.toString();
    }

    public void simulateCreateRoom() {
        this.roomCode = String.valueOf(100000 + random.nextInt(900000));
        this.activeHostTarget = isDraftZeroYield() ? HostTarget.SELF_HOSTED : draftHostTarget;
        this.activeMode = draftMode;
        this.activeMap = draftMap;
        this.activeDifficulty = draftDifficulty;
        this.activeThinkTime = draftThinkTime;
        this.activeRoomTag = draftRoomTag == null ? "" : draftRoomTag.trim();
        this.activeMaxPlayers = draftMaxPlayers;
        this.activePassword = sanitizeNumericPassword(draftPassword);
        this.activePrivacy = effectiveDraftPrivacy();
        this.activeRoomName = draftRoomName == null || draftRoomName.trim().isEmpty() ? "未命名星枢房间" : draftRoomName.trim();
        applyStatusPreset(PreviewStatus.RECRUITING);
    }

    public void refreshRoomCode() {
        this.roomCode = String.valueOf(100000 + random.nextInt(900000));
    }

    public void applyStatusPreset(PreviewStatus target) {
        this.status = target;
        for (int i = 0; i < 4; i++) {
            SquadSlot slot = slots.get(i);
            int seat = slot.seat;
            if (seat > activeMaxPlayers || target == PreviewStatus.IDLE) {
                clearSlot(slot);
            } else if (seat == 1) {
                slot.occupied = true;
                slot.host = true;
                slot.playerName = "星枢旅人";
                slot.uid = "10482910";
                slot.characterName = "帕露南";
                slot.ready = true;
            } else if (seat == 2) {
                slot.occupied = true;
                slot.host = false;
                slot.playerName = "枫糖布丁";
                slot.uid = "10938421";
                slot.characterName = "姬梦枫";
                slot.ready = true;
            } else if (target == PreviewStatus.READY_FULL) {
                slot.occupied = true;
                slot.host = false;
                slot.playerName = seat == 3 ? "夜风星愿" : "晚安星海";
                slot.uid = seat == 3 ? "11209344" : "11890452";
                slot.characterName = seat == 3 ? "米米" : "恋";
                slot.ready = true;
            } else {
                clearSlot(slot);
            }
        }
    }

    public void toggleSlotInteraction(int seat) {
        if (seat < 1 || seat > activeMaxPlayers) return;
        SquadSlot slot = slots.get(seat - 1);
        if (!slot.occupied) {
            slot.occupied = true;
            slot.host = (status == PreviewStatus.IDLE && seat == 1);
            slot.playerName = seat == 1 ? "星枢旅人" : ("测试队友 P" + seat);
            slot.uid = "10" + seat + "9420" + seat;
            slot.characterName = SAMPLE_HEROES[(seat - 1) % SAMPLE_HEROES.length];
            slot.ready = slot.host;
            recomputeStatus();
            return;
        }
        if (!slot.host) {
            slot.ready = !slot.ready;
        }
    }

    public boolean joinFirstOpenSlot() {
        for (SquadSlot slot : slots) {
            if (slot.seat <= activeMaxPlayers && !slot.occupied) {
                toggleSlotInteraction(slot.seat);
                return true;
            }
        }
        return false;
    }

    public int occupiedCount() {
        int count = 0;
        for (SquadSlot slot : slots) {
            if (slot.seat <= activeMaxPlayers && slot.occupied) count++;
        }
        return count;
    }

    public String buildInviteText() {
        String diff = activeDifficulty.isEmpty() ? "" : (" · " + activeDifficulty);
        String think = " · 思考:" + activeThinkTime.label;
        String tag = activeRoomTag.isEmpty() ? "" : (" · [" + activeRoomTag + "]");
        String yield = isActiveZeroYield() ? " · 自托管0收益" : "";
        Privacy eff = effectiveActivePrivacy();
        String pwd = eff == Privacy.PASSWORD ? ("｜密码:" + sanitizeNumericPassword(activePassword)) : "｜公开";
        return "【星枢组队邀请】房间号 #" + roomCode
            + "｜" + activeMode.commandToken + " · " + activeMap.name + diff + think + tag + yield + pwd
            + "｜当前 " + occupiedCount() + "/" + activeMaxPlayers + " 人"
            + "｜QQ 发送「趴加入 " + roomCode + "」查看";
    }

    private void recomputeStatus() {
        int count = occupiedCount();
        if (count <= 0) status = PreviewStatus.IDLE;
        else if (count >= activeMaxPlayers) status = PreviewStatus.READY_FULL;
        else status = PreviewStatus.RECRUITING;
    }

    private static void clearSlot(SquadSlot slot) {
        slot.occupied = false;
        slot.host = false;
        slot.playerName = "";
        slot.uid = "";
        slot.characterName = "";
        slot.ready = false;
    }
}
