package app.xingshu.installer;

import org.junit.Test;
import static org.junit.Assert.*;
import java.io.IOException;

public class AssistantLoginLinkTest {
    private final String token = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-abcde";

    @Test public void acceptsOnlyTheDedicatedAssistantHostAndCapability() throws Exception {
        assertEquals(43, token.length());
        assertEquals(token, AssistantLoginLink.parse("xingshu-installer://qq-login", token));
        assertEquals(token, AssistantLoginLink.parse("xingshu-installer://qq-login/#" + token, null));
    }

    @Test public void rejectsRedirectsMalformedTokensAndConflictingInputs() {
        String[] bad = {"https://evil.example/qq-login", "xingshu-installer://other", "xingshu-installer://qq-login/other",
            "xingshu-installer://user@qq-login", "xingshu-installer://qq-login:80", "xingshu-installer://qq-login?token=" + token};
        for (String value : bad) {
            try { AssistantLoginLink.parse(value, token); fail("accepted unexpected destination"); }
            catch (IOException expected) { assertFalse(expected.getMessage().contains(token)); }
        }
        for (String value : new String[] {"", "short", token + "x", token.replace('_', ' ')}) {
            try { AssistantLoginLink.parse("xingshu-installer://qq-login", value); fail("accepted invalid capability"); }
            catch (IOException expected) { }
        }
        try { AssistantLoginLink.parse("xingshu-installer://qq-login#" + token, token.replace('A', 'B')); fail(); }
        catch (IOException expected) { }
    }
}
