package app.xingshu.installer;

import android.content.Context;
import android.util.AtomicFile;
import java.io.*;
import java.math.BigInteger;
import java.security.*;
import java.security.cert.*;
import java.security.spec.PKCS8EncodedKeySpec;
import java.util.Date;
import org.bouncycastle.asn1.x500.X500Name;
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder;
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder;

/** One app-private identity. No pairing codes are saved or logged. */
final class PhoneIdentity {
    final PrivateKey key;
    final java.security.cert.Certificate certificate;
    PhoneIdentity(Context context) throws Exception {
        AtomicFile file=new AtomicFile(new File(context.getNoBackupFilesDir(),"local-adb-identity"));
        byte[] privateBytes,certBytes;
        if(file.getBaseFile().exists()) {
            try(DataInputStream in=new DataInputStream(file.openRead())) {
                privateBytes=read(in); certBytes=read(in);
            }
        } else {
            KeyPairGenerator generator=KeyPairGenerator.getInstance("RSA");
            generator.initialize(2048,new SecureRandom());
            java.security.KeyPair pair=generator.generateKeyPair();
            X500Name name=new X500Name("CN=XingShu Phone");
            long now=System.currentTimeMillis();
            certBytes=new JcaX509v3CertificateBuilder(name,new BigInteger(128,new SecureRandom()).setBit(127),
                new Date(now-86400000L),new Date(now+10L*365*86400000L),name,pair.getPublic())
                .build(new JcaContentSignerBuilder("SHA256withRSA").build(pair.getPrivate())).getEncoded();
            privateBytes=pair.getPrivate().getEncoded();
            FileOutputStream out=null;
            try {
                out=file.startWrite(); DataOutputStream data=new DataOutputStream(out);
                data.writeInt(privateBytes.length); data.write(privateBytes); data.writeInt(certBytes.length); data.write(certBytes);
                data.flush(); file.finishWrite(out);
            } catch(Exception e) { file.failWrite(out); throw e; }
        }
        key=KeyFactory.getInstance("RSA").generatePrivate(new PKCS8EncodedKeySpec(privateBytes));
        java.util.Arrays.fill(privateBytes,(byte)0);
        certificate=CertificateFactory.getInstance("X.509").generateCertificate(new ByteArrayInputStream(certBytes));
    }
    private static byte[] read(DataInputStream in) throws IOException {
        int size=in.readInt(); if(size<64||size>16384) throw new IOException("配对身份文件损坏，请清除助手数据后重新配对");
        byte[] data=new byte[size]; in.readFully(data); return data;
    }
}
