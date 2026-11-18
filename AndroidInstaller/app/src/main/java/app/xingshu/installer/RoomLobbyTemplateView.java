package app.xingshu.installer;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.graphics.Typeface;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.List;

/**
 * Interactive preview template for Android "趴开房" & room/squad information.
 * Uses MainActivity.AuroraUi directly to preserve the exact Aurora Light visual language.
 */
final class RoomLobbyTemplateView {
    private RoomLobbyTemplateView() { }

    static View build(Activity activity) {
        final RoomTemplateState state = new RoomTemplateState();
        LinearLayout col = new LinearLayout(activity);
        col.setOrientation(LinearLayout.VERTICAL);

        final TextView feedbackBanner = MainActivity.AuroraUi.callout(
            activity,
            "模板预览模式（未接入实时开房后端）：已对齐原版「普通/困难/噩梦/疯狂/极限」难度与思考时间，非官方现役活动标注「0 收益 · 自托管托管模式」。"
        );
        feedbackBanner.setBackground(MainActivity.AuroraUi.rounded(
            MainActivity.AuroraUi.ACCENT_SOFT,
            MainActivity.AuroraUi.ACCENT_BORDER,
            12,
            activity
        ));
        feedbackBanner.setTextColor(MainActivity.AuroraUi.ACCENT_INK);
        LinearLayout.LayoutParams fbLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        fbLp.topMargin = MainActivity.AuroraUi.dp(activity, 8);
        col.addView(feedbackBanner, fbLp);

        // Card 1: Current Room & P1-P4 Squad Preview
        LinearLayout roomCard = MainActivity.AuroraUi.card(activity);
        LinearLayout roomTopRow = new LinearLayout(activity);
        roomTopRow.setOrientation(LinearLayout.HORIZONTAL);
        roomTopRow.setGravity(Gravity.CENTER_VERTICAL);
        TextView roomCardTitle = MainActivity.AuroraUi.cardTitle(activity, "1. 当前房间与组队信息（模板）");
        roomCardTitle.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        final TextView statusPill = MainActivity.AuroraUi.pill(
            activity,
            "招募中",
            MainActivity.AuroraUi.ACCENT_SOFT,
            MainActivity.AuroraUi.ACCENT_BORDER,
            MainActivity.AuroraUi.ACCENT_INK
        );
        roomTopRow.addView(roomCardTitle);
        roomTopRow.addView(statusPill);
        roomCard.addView(roomTopRow);

        final TextView hostTargetBadge = MainActivity.AuroraUi.pill(
            activity,
            "官方现有模式",
            MainActivity.AuroraUi.OK_SOFT,
            MainActivity.AuroraUi.OK_BORDER,
            MainActivity.AuroraUi.OK_INK
        );
        LinearLayout.LayoutParams htLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WRAP_CONTENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        htLp.topMargin = MainActivity.AuroraUi.dp(activity, 6);
        roomCard.addView(hostTargetBadge, htLp);

        final TextView roomMetaView = MainActivity.AuroraUi.callout(activity, "");
        roomCard.addView(roomMetaView);

        final TextView zeroYieldRoomBanner = MainActivity.AuroraUi.callout(activity, RoomTemplateState.ZERO_YIELD_NOTICE);
        zeroYieldRoomBanner.setBackground(MainActivity.AuroraUi.rounded(
            MainActivity.AuroraUi.ACCENT_SOFT,
            MainActivity.AuroraUi.ACCENT_BORDER,
            10,
            activity
        ));
        zeroYieldRoomBanner.setTextColor(MainActivity.AuroraUi.ACCENT_INK);
        LinearLayout.LayoutParams zyRoomLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        zyRoomLp.bottomMargin = MainActivity.AuroraUi.dp(activity, 6);
        roomCard.addView(zeroYieldRoomBanner, zyRoomLp);

        final LinearLayout statusSwitchRow = new LinearLayout(activity);
        statusSwitchRow.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams swLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        swLp.bottomMargin = MainActivity.AuroraUi.dp(activity, 8);
        roomCard.addView(statusSwitchRow, swLp);

        final LinearLayout slotsContainer = new LinearLayout(activity);
        slotsContainer.setOrientation(LinearLayout.VERTICAL);
        roomCard.addView(slotsContainer);

        LinearLayout quickActionRow = new LinearLayout(activity);
        quickActionRow.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams qaLeft = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        qaLeft.rightMargin = MainActivity.AuroraUi.dp(activity, 4);
        LinearLayout.LayoutParams qaMid = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        qaMid.leftMargin = MainActivity.AuroraUi.dp(activity, 2);
        qaMid.rightMargin = MainActivity.AuroraUi.dp(activity, 2);
        LinearLayout.LayoutParams qaRight = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        qaRight.leftMargin = MainActivity.AuroraUi.dp(activity, 4);

        final Runnable[] renderRoomRef = new Runnable[1];

        Button copyCodeBtn = MainActivity.AuroraUi.button(activity, "复制房号", MainActivity.AuroraUi.BtnStyle.SECONDARY, v -> {
            copyToClipboard(activity, "xingshu_room_code", state.roomCode);
            feedbackBanner.setText("已复制预览房间号 #" + state.roomCode);
        });
        Button inviteBtn = MainActivity.AuroraUi.button(activity, "邀请队友", MainActivity.AuroraUi.BtnStyle.SECONDARY, v -> {
            copyToClipboard(activity, "xingshu_room_invite", state.buildInviteText());
            feedbackBanner.setText("已复制组队邀请文案（含房间号与 QQ 指令）");
        });
        Button joinBtn = MainActivity.AuroraUi.button(activity, "模拟加入", MainActivity.AuroraUi.BtnStyle.ACCENT, v -> {
            boolean joined = state.joinFirstOpenSlot();
            feedbackBanner.setText(joined ? "已为下一个空位模拟加入测试队友" : "当前预览房间已满员");
            renderRoomRef[0].run();
        });
        quickActionRow.addView(copyCodeBtn, qaLeft);
        quickActionRow.addView(inviteBtn, qaMid);
        quickActionRow.addView(joinBtn, qaRight);
        roomCard.addView(quickActionRow);
        col.addView(roomCard);

        // Card 2: 「趴开房」One-Tap Host Config Template
        LinearLayout hostCard = MainActivity.AuroraUi.card(activity);
        hostCard.addView(MainActivity.AuroraUi.cardTitle(activity, "2. 「趴开房」一键开房配置模板"));
        hostCard.addView(MainActivity.AuroraUi.caption(activity, "对齐游戏原版开房参数；非官方现役模式仅支持自托管（0收益）。"));

        hostCard.addView(sectionLabel(activity, "托管目标（官方 vs 自托管 0 收益）"));
        final LinearLayout hostTargetRow = new LinearLayout(activity);
        hostTargetRow.setOrientation(LinearLayout.HORIZONTAL);
        hostCard.addView(hostTargetRow);

        hostCard.addView(sectionLabel(activity, "对局模式"));
        final LinearLayout modeChipsContainer = new LinearLayout(activity);
        modeChipsContainer.setOrientation(LinearLayout.VERTICAL);
        hostCard.addView(modeChipsContainer);

        final TextView zeroYieldFormBanner = MainActivity.AuroraUi.callout(activity, RoomTemplateState.ZERO_YIELD_NOTICE);
        zeroYieldFormBanner.setBackground(MainActivity.AuroraUi.rounded(
            MainActivity.AuroraUi.ACCENT_SOFT,
            MainActivity.AuroraUi.ACCENT_BORDER,
            10,
            activity
        ));
        zeroYieldFormBanner.setTextColor(MainActivity.AuroraUi.ACCENT_INK);
        LinearLayout.LayoutParams zyFormLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        zyFormLp.topMargin = MainActivity.AuroraUi.dp(activity, 6);
        hostCard.addView(zeroYieldFormBanner, zyFormLp);

        hostCard.addView(sectionLabel(activity, "游玩地图"));
        final LinearLayout mapChipsContainer = new LinearLayout(activity);
        mapChipsContainer.setOrientation(LinearLayout.VERTICAL);
        hostCard.addView(mapChipsContainer);

        hostCard.addView(sectionLabel(activity, "难度（普通 / 困难 / 噩梦 / 疯狂 / 极限）与人数上限"));
        final LinearLayout diffChipsRow = new LinearLayout(activity);
        diffChipsRow.setOrientation(LinearLayout.HORIZONTAL);
        hostCard.addView(diffChipsRow);

        final LinearLayout maxPlayersRow = new LinearLayout(activity);
        maxPlayersRow.setOrientation(LinearLayout.HORIZONTAL);
        hostCard.addView(maxPlayersRow);

        hostCard.addView(sectionLabel(activity, "思考时间（快 / 中 / 慢）与房间标签"));
        final LinearLayout thinkTimeRow = new LinearLayout(activity);
        thinkTimeRow.setOrientation(LinearLayout.HORIZONTAL);
        hostCard.addView(thinkTimeRow);

        final LinearLayout roomTagRow = new LinearLayout(activity);
        roomTagRow.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams tagWrap = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        tagWrap.topMargin = MainActivity.AuroraUi.dp(activity, 6);
        hostCard.addView(roomTagRow, tagWrap);

        hostCard.addView(sectionLabel(activity, "密码（1-6位数字，不输入则为公开房间）"));
        final LinearLayout privacyRow = new LinearLayout(activity);
        privacyRow.setOrientation(LinearLayout.HORIZONTAL);
        hostCard.addView(privacyRow);

        final EditText passwordInput = MainActivity.AuroraUi.input(activity, "输入 1-6 位数字密码（留空则公开）", false);
        passwordInput.setInputType(InputType.TYPE_CLASS_NUMBER);
        passwordInput.setText(state.draftPassword);
        LinearLayout.LayoutParams pwLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        pwLp.topMargin = MainActivity.AuroraUi.dp(activity, 6);
        pwLp.bottomMargin = MainActivity.AuroraUi.dp(activity, 6);
        hostCard.addView(passwordInput, pwLp);

        hostCard.addView(sectionLabel(activity, "房间名称"));
        final EditText roomNameInput = MainActivity.AuroraUi.input(activity, "例如：日常刷本速车 · 来不抢输出", false);
        roomNameInput.setText(state.draftRoomName);
        LinearLayout.LayoutParams rnLp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        rnLp.bottomMargin = MainActivity.AuroraUi.dp(activity, 8);
        hostCard.addView(roomNameInput, rnLp);

        final TextView cmdPreviewView = MainActivity.AuroraUi.callout(activity, "");
        cmdPreviewView.setTypeface(Typeface.MONOSPACE);
        hostCard.addView(cmdPreviewView);

        final Runnable[] renderFormRef = new Runnable[1];

        passwordInput.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) { }
            @Override public void afterTextChanged(Editable s) {
                String clean = RoomTemplateState.sanitizeNumericPassword(s.toString());
                if (!clean.equals(s.toString())) {
                    s.replace(0, s.length(), clean);
                    return;
                }
                state.draftPassword = clean;
                cmdPreviewView.setText("生成指令：\n" + state.buildPaKaiFangCommand());
            }
        });
        roomNameInput.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) { }
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) { }
            @Override public void afterTextChanged(Editable s) {
                state.draftRoomName = s.toString();
                cmdPreviewView.setText("生成指令：\n" + state.buildPaKaiFangCommand());
            }
        });

        hostCard.addView(MainActivity.AuroraUi.button(activity, "模拟开房并刷新上方房间卡片", MainActivity.AuroraUi.BtnStyle.PRIMARY, v -> {
            state.simulateCreateRoom();
            feedbackBanner.setText("已按当前配置模拟开房 #" + state.roomCode + (state.isActiveZeroYield() ? "（自托管 · 0收益）" : "（官方现有模式）"));
            renderRoomRef[0].run();
        }));
        hostCard.addView(MainActivity.AuroraUi.button(activity, "复制「趴开房」指令", MainActivity.AuroraUi.BtnStyle.SECONDARY, v -> {
            String cmd = state.buildPaKaiFangCommand();
            copyToClipboard(activity, "xingshu_pakaifang_cmd", cmd);
            feedbackBanner.setText("已复制指令：" + cmd);
        }));
        col.addView(hostCard);

        // Card 3: Cross-platform placeholder info
        LinearLayout linkCard = MainActivity.AuroraUi.card(activity);
        linkCard.addView(MainActivity.AuroraUi.cardTitle(activity, "3. 跨端联动占位（Hub / QQ Bot）"));
        linkCard.addView(MainActivity.AuroraUi.callout(
            activity,
            "• 星枢 Hub：「星枢核心 → 房间与组队」同步对齐原版难度/思考时间与 0 收益自托管标识\n"
            + "• QQ Bot：同步支持「普通/困难/噩梦/疯狂/极限」（兼容恶梦）与 0 收益自托管提示\n"
            + "• 后续接入：自托管服务器模式下所有对局严格保持 0 官方收益隔离"
        ));
        col.addView(linkCard);

        renderRoomRef[0] = () -> {
            if (state.status == RoomTemplateState.PreviewStatus.READY_FULL) {
                MainActivity.AuroraUi.tintPill(statusPill, MainActivity.AuroraUi.OK_SOFT, MainActivity.AuroraUi.OK_BORDER, MainActivity.AuroraUi.OK_INK, activity);
            } else if (state.status == RoomTemplateState.PreviewStatus.RECRUITING) {
                MainActivity.AuroraUi.tintPill(statusPill, MainActivity.AuroraUi.ACCENT_SOFT, MainActivity.AuroraUi.ACCENT_BORDER, MainActivity.AuroraUi.ACCENT_INK, activity);
            } else {
                MainActivity.AuroraUi.tintPill(statusPill, MainActivity.AuroraUi.SURFACE_SUBTLE, MainActivity.AuroraUi.BORDER, MainActivity.AuroraUi.INK_MUTED, activity);
            }
            statusPill.setText(state.status.label + " · " + state.occupiedCount() + "/" + state.activeMaxPlayers);

            boolean zeroYield = state.isActiveZeroYield();
            if (zeroYield) {
                MainActivity.AuroraUi.tintPill(hostTargetBadge, MainActivity.AuroraUi.ACCENT_SOFT, MainActivity.AuroraUi.ACCENT_BORDER, MainActivity.AuroraUi.ACCENT_INK, activity);
                hostTargetBadge.setText("自托管服务器 · 0收益 (原型占位)");
            } else {
                MainActivity.AuroraUi.tintPill(hostTargetBadge, MainActivity.AuroraUi.OK_SOFT, MainActivity.AuroraUi.OK_BORDER, MainActivity.AuroraUi.OK_INK, activity);
                hostTargetBadge.setText("官方现有模式");
            }
            zeroYieldRoomBanner.setVisibility(zeroYield ? View.VISIBLE : View.GONE);

            String diff = state.activeDifficulty.isEmpty() ? "" : (" · " + state.activeDifficulty);
            String tag = state.activeRoomTag.isEmpty() ? "" : (" · [" + state.activeRoomTag + "]");
            RoomTemplateState.Privacy effPrivacy = state.effectiveActivePrivacy();
            String pwd = effPrivacy == RoomTemplateState.Privacy.PASSWORD
                ? ("密码 " + RoomTemplateState.sanitizeNumericPassword(state.activePassword))
                : effPrivacy.label;
            roomMetaView.setText(
                "房间号：#" + state.roomCode + "  |  " + state.activeRoomName + "\n"
                + state.activeMode.label + " · " + state.activeMap.name + diff
                + " · 思考:" + state.activeThinkTime.label + tag + " · " + pwd
            );

            statusSwitchRow.removeAllViews();
            for (RoomTemplateState.PreviewStatus preset : RoomTemplateState.PreviewStatus.values()) {
                boolean active = state.status == preset;
                TextView pill = buildChoicePill(activity, preset.label, active, v -> {
                    state.applyStatusPreset(preset);
                    feedbackBanner.setText("已切换预览状态为「" + preset.label + "」");
                    renderRoomRef[0].run();
                });
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                lp.rightMargin = MainActivity.AuroraUi.dp(activity, 4);
                statusSwitchRow.addView(pill, lp);
            }

            slotsContainer.removeAllViews();
            for (RoomTemplateState.SquadSlot slot : state.slots) {
                boolean closed = slot.seat > state.activeMaxPlayers;
                LinearLayout row = new LinearLayout(activity);
                row.setOrientation(LinearLayout.HORIZONTAL);
                row.setGravity(Gravity.CENTER_VERTICAL);
                row.setBackground(MainActivity.AuroraUi.rounded(
                    slot.occupied ? MainActivity.AuroraUi.SURFACE : MainActivity.AuroraUi.SURFACE_SUBTLE,
                    MainActivity.AuroraUi.BORDER,
                    11,
                    activity
                ));
                int pad = MainActivity.AuroraUi.dp(activity, 10);
                row.setPadding(pad, MainActivity.AuroraUi.dp(activity, 8), pad, MainActivity.AuroraUi.dp(activity, 8));
                LinearLayout.LayoutParams rowLp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT
                );
                rowLp.bottomMargin = MainActivity.AuroraUi.dp(activity, 6);

                TextView seatTv = MainActivity.AuroraUi.pill(
                    activity,
                    "P" + slot.seat,
                    slot.host ? MainActivity.AuroraUi.INK : MainActivity.AuroraUi.SURFACE_SUBTLE,
                    MainActivity.AuroraUi.BORDER,
                    slot.host ? 0xFFFFFFFF : MainActivity.AuroraUi.INK
                );
                LinearLayout.LayoutParams sLp = (LinearLayout.LayoutParams) seatTv.getLayoutParams();
                sLp.rightMargin = MainActivity.AuroraUi.dp(activity, 8);
                row.addView(seatTv);

                TextView infoTv = new TextView(activity);
                infoTv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
                if (closed) {
                    infoTv.setText("未开放（上限 " + state.activeMaxPlayers + " 人）");
                    infoTv.setTextColor(MainActivity.AuroraUi.INK_SUBTLE);
                } else if (slot.occupied) {
                    infoTv.setText(slot.playerName + " · " + slot.characterName + "\nUID " + slot.uid);
                    infoTv.setTextColor(MainActivity.AuroraUi.INK);
                } else {
                    infoTv.setText("空位待加入（点击模拟入队）");
                    infoTv.setTextColor(MainActivity.AuroraUi.INK_MUTED);
                }
                row.addView(infoTv, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

                if (!closed) {
                    String badgeText = !slot.occupied ? "+ 加入" : (slot.host ? "房主" : (slot.ready ? "已准备" : "未准备"));
                    TextView stateTag = MainActivity.AuroraUi.pill(
                        activity,
                        badgeText,
                        slot.occupied && slot.ready ? MainActivity.AuroraUi.OK_SOFT : MainActivity.AuroraUi.ACCENT_SOFT,
                        slot.occupied && slot.ready ? MainActivity.AuroraUi.OK_BORDER : MainActivity.AuroraUi.ACCENT_BORDER,
                        slot.occupied && slot.ready ? MainActivity.AuroraUi.OK_INK : MainActivity.AuroraUi.ACCENT_INK
                    );
                    row.addView(stateTag);
                    row.setOnClickListener(v -> {
                        state.toggleSlotInteraction(slot.seat);
                        renderRoomRef[0].run();
                    });
                }
                slotsContainer.addView(row, rowLp);
            }
        };

        renderFormRef[0] = () -> {
            hostTargetRow.removeAllViews();
            RoomTemplateState.HostTarget effectiveTarget = state.isDraftZeroYield()
                ? RoomTemplateState.HostTarget.SELF_HOSTED
                : state.draftHostTarget;
            for (RoomTemplateState.HostTarget t : RoomTemplateState.HostTarget.values()) {
                TextView chip = buildChoicePill(activity, t.label, effectiveTarget == t, v -> {
                    state.setDraftHostTarget(t);
                    renderFormRef[0].run();
                });
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                lp.rightMargin = MainActivity.AuroraUi.dp(activity, 5);
                hostTargetRow.addView(chip, lp);
            }

            modeChipsContainer.removeAllViews();
            RoomTemplateState.Mode[] modes = RoomTemplateState.Mode.values();
            LinearLayout modeRow = null;
            for (int i = 0; i < modes.length; i++) {
                if (i % 2 == 0) {
                    modeRow = new LinearLayout(activity);
                    modeRow.setOrientation(LinearLayout.HORIZONTAL);
                    LinearLayout.LayoutParams rLp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT
                    );
                    rLp.bottomMargin = MainActivity.AuroraUi.dp(activity, 5);
                    modeChipsContainer.addView(modeRow, rLp);
                }
                final RoomTemplateState.Mode m = modes[i];
                TextView chip = buildChoicePill(activity, m.label, state.draftMode == m, v -> {
                    state.setDraftMode(m);
                    renderFormRef[0].run();
                });
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                lp.rightMargin = MainActivity.AuroraUi.dp(activity, 5);
                modeRow.addView(chip, lp);
            }

            zeroYieldFormBanner.setVisibility(state.isDraftZeroYield() ? View.VISIBLE : View.GONE);

            mapChipsContainer.removeAllViews();
            List<RoomTemplateState.MapPreset> maps = state.mapsForDraftMode();
            LinearLayout currentRow = null;
            for (int i = 0; i < maps.size(); i++) {
                if (i % 2 == 0) {
                    currentRow = new LinearLayout(activity);
                    currentRow.setOrientation(LinearLayout.HORIZONTAL);
                    LinearLayout.LayoutParams rLp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT
                    );
                    rLp.bottomMargin = MainActivity.AuroraUi.dp(activity, 5);
                    mapChipsContainer.addView(currentRow, rLp);
                }
                final RoomTemplateState.MapPreset m = maps.get(i);
                TextView chip = buildChoicePill(activity, m.name, state.draftMap.id == m.id, v -> {
                    state.setDraftMap(m);
                    renderFormRef[0].run();
                });
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                lp.rightMargin = MainActivity.AuroraUi.dp(activity, 5);
                currentRow.addView(chip, lp);
            }

            diffChipsRow.removeAllViews();
            if (state.draftMap.difficulties.isEmpty()) {
                TextView hint = MainActivity.AuroraUi.caption(activity, "当前地图无需选择难度");
                diffChipsRow.addView(hint);
            } else {
                for (String diff : state.draftMap.difficulties) {
                    TextView chip = buildChoicePill(activity, diff, diff.equals(state.draftDifficulty), v -> {
                        state.draftDifficulty = diff;
                        renderFormRef[0].run();
                    });
                    LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                    lp.rightMargin = MainActivity.AuroraUi.dp(activity, 4);
                    diffChipsRow.addView(chip, lp);
                }
            }

            maxPlayersRow.removeAllViews();
            LinearLayout.LayoutParams mpWrap = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT
            );
            mpWrap.topMargin = MainActivity.AuroraUi.dp(activity, 6);
            maxPlayersRow.setLayoutParams(mpWrap);
            for (int n : new int[] { 2, 3, 4 }) {
                final int maxP = n;
                TextView chip = buildChoicePill(activity, maxP + " 人房", state.draftMaxPlayers == maxP, v -> {
                    state.draftMaxPlayers = maxP;
                    renderFormRef[0].run();
                });
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                lp.rightMargin = MainActivity.AuroraUi.dp(activity, 5);
                maxPlayersRow.addView(chip, lp);
            }

            thinkTimeRow.removeAllViews();
            for (RoomTemplateState.ThinkTime t : RoomTemplateState.ThinkTime.values()) {
                TextView chip = buildChoicePill(activity, "思考:" + t.label, state.draftThinkTime == t, v -> {
                    state.draftThinkTime = t;
                    renderFormRef[0].run();
                });
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                lp.rightMargin = MainActivity.AuroraUi.dp(activity, 5);
                thinkTimeRow.addView(chip, lp);
            }

            roomTagRow.removeAllViews();
            for (final String tag : RoomTemplateState.ROOM_TAGS) {
                TextView chip = buildChoicePill(activity, tag, tag.equals(state.draftRoomTag), v -> {
                    state.draftRoomTag = tag;
                    renderFormRef[0].run();
                });
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                lp.rightMargin = MainActivity.AuroraUi.dp(activity, 4);
                roomTagRow.addView(chip, lp);
            }

            privacyRow.removeAllViews();
            for (RoomTemplateState.Privacy p : RoomTemplateState.Privacy.values()) {
                TextView chip = buildChoicePill(activity, p.label, state.draftPrivacy == p, v -> {
                    state.draftPrivacy = p;
                    renderFormRef[0].run();
                });
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
                lp.rightMargin = MainActivity.AuroraUi.dp(activity, 5);
                privacyRow.addView(chip, lp);
            }
            passwordInput.setVisibility(state.draftPrivacy == RoomTemplateState.Privacy.PASSWORD ? View.VISIBLE : View.GONE);
            cmdPreviewView.setText("生成指令：\n" + state.buildPaKaiFangCommand());
        };

        renderRoomRef[0].run();
        renderFormRef[0].run();
        return col;
    }

    private static TextView sectionLabel(Activity activity, String text) {
        TextView tv = new TextView(activity);
        tv.setText(text);
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        tv.setTypeface(Typeface.DEFAULT_BOLD);
        tv.setTextColor(MainActivity.AuroraUi.INK_MUTED);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT
        );
        lp.topMargin = MainActivity.AuroraUi.dp(activity, 8);
        lp.bottomMargin = MainActivity.AuroraUi.dp(activity, 4);
        tv.setLayoutParams(lp);
        return tv;
    }

    private static TextView buildChoicePill(Activity activity, String label, boolean selected, View.OnClickListener onClick) {
        TextView tv = new TextView(activity);
        tv.setText(label);
        tv.setGravity(Gravity.CENTER);
        tv.setSingleLine(true);
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        tv.setTypeface(selected ? Typeface.DEFAULT_BOLD : Typeface.DEFAULT);
        tv.setPadding(
            MainActivity.AuroraUi.dp(activity, 6),
            MainActivity.AuroraUi.dp(activity, 7),
            MainActivity.AuroraUi.dp(activity, 6),
            MainActivity.AuroraUi.dp(activity, 7)
        );
        tv.setBackground(MainActivity.AuroraUi.rounded(
            selected ? MainActivity.AuroraUi.INK : MainActivity.AuroraUi.SURFACE_SUBTLE,
            selected ? MainActivity.AuroraUi.INK : MainActivity.AuroraUi.BORDER,
            9,
            activity
        ));
        tv.setTextColor(selected ? 0xFFFFFFFF : MainActivity.AuroraUi.INK);
        tv.setOnClickListener(onClick);
        return tv;
    }

    private static void copyToClipboard(Activity activity, String label, String text) {
        ClipboardManager cm = (ClipboardManager) activity.getSystemService(Activity.CLIPBOARD_SERVICE);
        if (cm != null) {
            cm.setPrimaryClip(ClipData.newPlainText(label, text));
        }
    }
}
