using System;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Cryptography;
using Crestron.SimplSharp.CrestronIO;

namespace FireTVADB_3Series
{
    internal class AdbAuth : IDisposable
    {
        private RSACryptoServiceProvider _rsa;
        private readonly string          _keyPath;

        public AdbAuth(string keyPath)
        {
            _keyPath = keyPath;
        }

        public void Initialize()
        {
            CrestronConsole.PrintLine("[ADB-Auth] KeyPath=\"" +
                (_keyPath ?? "(null)") + "\"");

            if (!string.IsNullOrEmpty(_keyPath))
            {
                try
                {
                    bool exists = File.Exists(_keyPath);
                    CrestronConsole.PrintLine("[ADB-Auth] File.Exists => " + exists);

                    if (exists)
                    {
                        RSAParameters p = ReadKeyFile(_keyPath);
                        var rsa = new RSACryptoServiceProvider(2048);
                        rsa.ImportParameters(p);
                        _rsa = rsa;
                        CrestronConsole.PrintLine("[ADB-Auth] Loaded existing key (" +
                            p.Modulus.Length + "-byte modulus)");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    CrestronConsole.PrintLine("[ADB-Auth] Load failed: " + ex.Message);
                    // Fall through to generate new key
                }
            }

            CrestronConsole.PrintLine("[ADB-Auth] Generating new 2048-bit RSA key...");
            _rsa = new RSACryptoServiceProvider(2048);

            if (!string.IsNullOrEmpty(_keyPath))
            {
                try
                {
                    EnsureDirectory(_keyPath);
                    RSAParameters p = _rsa.ExportParameters(true);
                    WriteKeyFile(_keyPath, p);
                    CrestronConsole.PrintLine("[ADB-Auth] Key saved to " + _keyPath);
                }
                catch (Exception ex)
                {
                    CrestronConsole.PrintLine("[ADB-Auth] Save failed: " + ex.Message);
                }
            }
        }

        // ADB AUTH: the 20-byte token IS the digest value — do NOT hash it again.
        // SignHash wraps it in DigestInfo(SHA1 OID) + PKCS#1 v1.5 padding,
        // then applies the RSA private key — matching OpenSSL RSA_sign(NID_sha1,...).
        public byte[] SignToken(byte[] token)
        {
            return _rsa.SignHash(token, "1.3.14.3.2.26");
        }

        public byte[] GetPublicKeyPayload()
        {
            return AdbAndroidKey.BuildPayload(_rsa.ExportParameters(false));
        }

        public void Dispose()
        {
            if (_rsa != null) { _rsa.Dispose(); _rsa = null; }
        }

        // ── RSAParameters XML serialisation ──────────────────────────────────

        private static void WriteKeyFile(string path, RSAParameters p)
        {
            var sb = new StringBuilder();
            sb.Append("<RSAKey>");
            AppendField(sb, "Modulus",  p.Modulus);
            AppendField(sb, "Exponent", p.Exponent);
            AppendField(sb, "D",        p.D);
            AppendField(sb, "P",        p.P);
            AppendField(sb, "Q",        p.Q);
            AppendField(sb, "DP",       p.DP);
            AppendField(sb, "DQ",       p.DQ);
            AppendField(sb, "InverseQ", p.InverseQ);
            sb.Append("</RSAKey>");

            using (StreamWriter sw = File.CreateText(path))
                sw.Write(sb.ToString());
        }

        private static RSAParameters ReadKeyFile(string path)
        {
            string xml;
            using (StreamReader sr = File.OpenText(path))
                xml = sr.ReadToEnd();

            RSAParameters p = new RSAParameters();
            p.Modulus   = ReadField(xml, "Modulus");
            p.Exponent  = ReadField(xml, "Exponent");
            p.D         = ReadField(xml, "D");
            p.P         = ReadField(xml, "P");
            p.Q         = ReadField(xml, "Q");
            p.DP        = ReadField(xml, "DP");
            p.DQ        = ReadField(xml, "DQ");
            p.InverseQ  = ReadField(xml, "InverseQ");
            return p;
        }

        private static void AppendField(StringBuilder sb, string tag, byte[] val)
        {
            sb.Append('<').Append(tag).Append('>');
            if (val != null) sb.Append(Convert.ToBase64String(val));
            sb.Append("</").Append(tag).Append('>');
        }

        private static byte[] ReadField(string xml, string tag)
        {
            string open  = "<"  + tag + ">";
            string close = "</" + tag + ">";
            int s = xml.IndexOf(open);
            int e = xml.IndexOf(close);
            if (s < 0 || e < 0) return null;
            s += open.Length;
            string b64 = xml.Substring(s, e - s);
            return b64.Length > 0 ? Convert.FromBase64String(b64) : null;
        }

        private static void EnsureDirectory(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
