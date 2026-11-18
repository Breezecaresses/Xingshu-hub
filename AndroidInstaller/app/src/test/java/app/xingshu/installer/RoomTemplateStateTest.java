package app.xingshu.installer;

import org.junit.Test;
import static org.junit.Assert.*;

public final class RoomTemplateStateTest {
    @Test
    public void generatesExpectedCommandAndHandlesStatusPresets() {
        RoomTemplateState state = new RoomTemplateState();
        String cmd = state.buildPaKaiFangCommand();
        assertTrue(cmd.startsWith("趴开房 PVE 星趴·梦想号 噩梦 思考:中 标签:速刷 4人 密码:2580"));
        assertFalse(state.isDraftZeroYield());

        state.applyStatusPreset(RoomTemplateState.PreviewStatus.IDLE);
        assertEquals(0, state.occupiedCount());

        state.applyStatusPreset(RoomTemplateState.PreviewStatus.READY_FULL);
        assertEquals(4, state.occupiedCount());

        state.setDraftMode(RoomTemplateState.Mode.PVP);
        assertEquals("", state.draftDifficulty);
        assertTrue(state.buildPaKaiFangCommand().contains("趴开房 PVP "));
    }

    @Test
    public void enforcesOfficialDifficultiesZeroYieldSelfHostedAndEmptyPasswordPublic() {
        RoomTemplateState state = new RoomTemplateState();
        assertEquals("噩梦", state.draftDifficulty);
        assertTrue(state.draftMap.difficulties.contains("普通"));
        assertTrue(state.draftMap.difficulties.contains("极限"));

        state.draftPassword = "";
        assertEquals(RoomTemplateState.Privacy.PUBLIC, state.effectiveDraftPrivacy());
        assertTrue(state.buildPaKaiFangCommand().contains(" 公开 "));

        state.setDraftMode(RoomTemplateState.Mode.COOP_EVENT);
        assertTrue(state.isDraftZeroYield());
        assertEquals(RoomTemplateState.HostTarget.SELF_HOSTED, state.draftHostTarget);
        assertTrue(state.buildPaKaiFangCommand().contains("自托管0收益"));

        state.simulateCreateRoom();
        assertTrue(state.isActiveZeroYield());
        assertTrue(state.buildInviteText().contains("自托管0收益"));
    }

    @Test
    public void simulatesJoiningAndTogglingReadiness() {
        RoomTemplateState state = new RoomTemplateState();
        assertEquals(2, state.occupiedCount());
        assertTrue(state.joinFirstOpenSlot());
        assertEquals(3, state.occupiedCount());

        state.toggleSlotInteraction(3);
        assertTrue(state.slots.get(2).ready);
    }
}
