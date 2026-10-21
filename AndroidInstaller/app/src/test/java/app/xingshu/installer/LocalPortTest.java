package app.xingshu.installer;

import org.junit.Test;
import static org.junit.Assert.*;

public final class LocalPortTest {
    @Test public void acceptsPortAndFullAddress() {
        assertEquals(33399,LocalPort.parse("33399"));
        assertEquals(45678,LocalPort.parse(" 192.168.31.87:45678 "));
        assertEquals(1024,LocalPort.parse("1024")); assertEquals(65535,LocalPort.parse("65535"));
    }
    @Test public void rejectsMissingOrInvalidPort() {
        for(String value:new String[]{"","192.168.31.87","123","65536","999.168.1.1:45678","33399;id","localhost:45678","123456"}) {
            try { LocalPort.parse(value); fail("accepted "+value); } catch(IllegalArgumentException expected) { }
        }
    }
}
