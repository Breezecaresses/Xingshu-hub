import java.nio.file.*;
import java.security.*;
import java.util.*;

/** Uses the existing APK signing identity. Passwords and keys never enter the repository. */
class SignUpdate {
    public static void main(String[] a) throws Exception {
        if (a[0].equals("--verify")) {
            var cert = java.security.cert.CertificateFactory.getInstance("X.509").generateCertificate(
                new java.io.ByteArrayInputStream(Base64.getDecoder().decode(Files.readString(Path.of(a[1])).trim())));
            Signature check = Signature.getInstance("SHA256withRSA"); check.initVerify(cert); check.update(Files.readAllBytes(Path.of(a[2])));
            if (!check.verify(Base64.getDecoder().decode(Files.readString(Path.of(a[3])).trim()))) throw new SecurityException("Update signature mismatch");
            return;
        }
        char[] password = Files.readString(Path.of(a[1])).trim().toCharArray();
        KeyStore store = KeyStore.getInstance("JKS");
        try (var in = Files.newInputStream(Path.of(a[0]))) { store.load(in, password); }
        if (a.length == 2) {
            System.out.print(Base64.getEncoder().encodeToString(store.getCertificate("xingshu").getEncoded()));
        } else {
            Signature signer = Signature.getInstance("SHA256withRSA");
            signer.initSign((PrivateKey) store.getKey("xingshu", password));
            signer.update(Files.readAllBytes(Path.of(a[2])));
            Files.writeString(Path.of(a[3]), Base64.getEncoder().encodeToString(signer.sign()));
        }
        Arrays.fill(password, '\0');
    }
}
