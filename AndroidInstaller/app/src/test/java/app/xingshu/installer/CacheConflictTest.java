package app.xingshu.installer;

import org.junit.Test;
import org.junit.Rule;
import org.junit.rules.TemporaryFolder;
import java.io.*;
import java.nio.file.Files;
import java.security.MessageDigest;
import static org.junit.Assert.*;

public class CacheConflictTest {
    @Rule public TemporaryFolder temporary = new TemporaryFolder();
    private String hash(File file) throws Exception {
        StringBuilder value=new StringBuilder();
        for(byte b:MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(file.toPath())))
            value.append(String.format("%02x",b&255));
        return value.toString();
    }
    private File file(String name,byte[] bytes) throws Exception {
        File file=temporary.newFile(name); Files.write(file.toPath(),bytes); return file;
    }
    @Test public void resourceCatalogAndContainerMustBothMatch() throws Exception {
        byte[] bytes=new byte[256]; System.arraycopy(new byte[]{'U','n','i','t','y','F','S',0},0,bytes,0,8);
        File target=file("target",bytes), catalog=file("catalog",new byte[]{1,2,3});
        assertTrue(CacheConflict.replaceable(target,catalog,hash(catalog)));
        assertFalse(CacheConflict.replaceable(target,catalog,new String(new char[64]).replace('\0','0')));
        assertFalse(CacheConflict.replaceable(target,catalog,""));
        bytes[0]='X'; Files.write(target.toPath(),bytes);
        assertFalse(CacheConflict.replaceable(target,catalog,hash(catalog)));
    }
    @Test public void conflictArchiveKeepsExactBytesAcrossRepeatedAndLaterInstalls() throws Exception {
        File target=file("target",new byte[]{4,5,6}), backups=temporary.newFolder("backups");
        String oldHash=hash(target); File old=CacheConflict.archive(target,backups,oldHash);
        assertArrayEquals(new byte[]{4,5,6},Files.readAllBytes(old.toPath()));
        assertTrue(new File(old.getParentFile(),"conflict.properties").isFile());
        assertEquals(old,CacheConflict.archive(target,backups,oldHash));
        Files.write(target.toPath(),new byte[]{7,8,9});
        File next=CacheConflict.archive(target,backups,hash(target));
        assertNotEquals(old,next);
        assertArrayEquals(new byte[]{4,5,6},Files.readAllBytes(old.toPath()));
        assertArrayEquals(new byte[]{7,8,9},Files.readAllBytes(target.toPath()));
    }
    @Test public void changedOrCorruptBackupBlocksInstallation() throws Exception {
        File target=file("target",new byte[]{1,2}), backups=temporary.newFolder("backups");
        String before=hash(target); Files.write(target.toPath(),new byte[]{9,9});
        try { CacheConflict.archive(target,backups,before); fail("changed source accepted"); }
        catch(IOException expected) { assertTrue(expected.getMessage().contains("校验失败")); }
        File saved=CacheConflict.archive(target,backups,hash(target));
        Files.write(saved.toPath(),new byte[]{0});
        try { CacheConflict.archive(target,backups,hash(target)); fail("corrupt backup accepted"); }
        catch(IOException expected) { assertTrue(expected.getMessage().contains("备份损坏")); }
        assertArrayEquals(new byte[]{9,9},Files.readAllBytes(target.toPath()));
    }
}
