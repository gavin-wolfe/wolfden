using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Wolfden.Git;

/// <summary>Which kind of signature a commit or tag carries.</summary>
public enum SignatureKind
{
    /// <summary>An OpenPGP signature, made with gpg (git's default).</summary>
    OpenPgp,

    /// <summary>An SSH signature (<c>gpg.format=ssh</c>).</summary>
    Ssh,

    /// <summary>An X.509 (S/MIME) signature (<c>gpg.format=x509</c>, gpgsm).</summary>
    X509,

    /// <summary>A signature Wolfden doesn't recognize, or couldn't read.</summary>
    Unknown,
}

/// <summary>
/// Which key made a signature, read from the signature's own bytes, without
/// running gpg or ssh-keygen, so it's quick for every commit in a long
/// history (verifying each one with gpg took about a tenth of a second).
/// It says which key, not whether the signature is good.
/// </summary>
/// <param name="Kind">The kind of signature.</param>
/// <param name="KeyId">
/// The key: an OpenPGP key's fingerprint in hex (or its 16-hex-digit key ID,
/// when the signature names only that), or an SSH key's
/// <c>SHA256:</c> fingerprint as <c>ssh-keygen -l</c> prints it. Null when
/// the signature doesn't say, or couldn't be read.
/// </param>
/// <param name="KeyIdIsShort">True when <paramref name="KeyId"/> is an OpenPGP key ID rather than a full fingerprint.</param>
public sealed record SignatureKey(SignatureKind Kind, string? KeyId, bool KeyIdIsShort = false)
{
    /// <summary>
    /// Reads an armored signature, as git stores it in a commit's
    /// <c>gpgsig</c> header or at the end of a tag, with the header's
    /// leading spaces already taken off each line.
    /// </summary>
    public static SignatureKey Read(string armored)
    {
        ArgumentNullException.ThrowIfNull(armored);
        var lines = armored.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var begin = Array.FindIndex(lines, l => l.StartsWith("-----BEGIN ", StringComparison.Ordinal));
        if (begin < 0)
        {
            return new SignatureKey(SignatureKind.Unknown, null);
        }

        var kind = lines[begin].Trim() switch
        {
            "-----BEGIN PGP SIGNATURE-----" => SignatureKind.OpenPgp,
            "-----BEGIN SSH SIGNATURE-----" => SignatureKind.Ssh,
            "-----BEGIN SIGNED MESSAGE-----" => SignatureKind.X509,
            _ => SignatureKind.Unknown,
        };
        if (kind is SignatureKind.X509 or SignatureKind.Unknown)
        {
            return new SignatureKey(kind, null);
        }

        var bytes = Body(lines, begin + 1, armorHeaders: kind == SignatureKind.OpenPgp);
        if (bytes is null)
        {
            return new SignatureKey(kind, null);
        }

        try
        {
            return kind == SignatureKind.OpenPgp ? OpenPgpKey(bytes) : SshKey(bytes);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException or OverflowException)
        {
            // Cut short or malformed: the kind is still known.
            return new SignatureKey(kind, null);
        }
    }

    /// <summary>The base64 body after the BEGIN line, up to the checksum or END line, or null if it isn't base64.</summary>
    private static byte[]? Body(string[] lines, int start, bool armorHeaders)
    {
        var i = start;
        if (armorHeaders && lines.Skip(start).Any(l => l.Trim().Length == 0))
        {
            // OpenPGP armor may have "Key: value" headers, ended by a blank line.
            var blank = Array.FindIndex(lines, start, l => l.Trim().Length == 0);
            if (lines.Skip(start).Take(blank - start).All(l => l.Contains(": ", StringComparison.Ordinal)))
            {
                i = blank + 1;
            }
        }

        var text = new StringBuilder();
        for (; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("-----END ", StringComparison.Ordinal) || (armorHeaders && line.StartsWith('=')))
            {
                break;
            }

            text.Append(line);
        }

        try
        {
            return Convert.FromBase64String(text.ToString());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The issuer of the first signature packet (RFC 9580 §5.2): its fingerprint subpacket (33), else its key ID (16, or a v3 packet's).</summary>
    private static SignatureKey OpenPgpKey(byte[] data)
    {
        var at = 0;
        while (at < data.Length)
        {
            var header = data[at++];
            if ((header & 0x80) == 0)
            {
                break;
            }

            int tag;
            long length;
            if ((header & 0x40) != 0)
            {
                // New format.
                tag = header & 0x3F;
                length = NewLength(data, ref at);
            }
            else
            {
                tag = (header >> 2) & 0x0F;
                switch (header & 0x03)
                {
                    case 0:
                        length = data[at++];
                        break;
                    case 1:
                        length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at, 2));
                        at += 2;
                        break;
                    case 2:
                        length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at, 4));
                        at += 4;
                        break;
                    default:
                        // Indeterminate: the packet runs to the end.
                        length = data.Length - at;
                        break;
                }
            }

            if (tag == 2)
            {
                return SignaturePacket(data.AsSpan(at, (int)Math.Min(length, data.Length - at)));
            }

            at += (int)length;
        }

        return new SignatureKey(SignatureKind.OpenPgp, null);
    }

    private static long NewLength(ReadOnlySpan<byte> data, ref int at, bool subpacket = false)
    {
        var first = data[at++];
        if (first < 192)
        {
            return first;
        }

        if (first < 224 || (subpacket && first < 255))
        {
            return ((first - 192) << 8) + data[at++] + 192;
        }

        if (first == 255)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
            at += 4;
            return length;
        }

        // A partial body length: a signature isn't sent that way; read what's here.
        return data.Length - at;
    }

    private static SignatureKey SignaturePacket(ReadOnlySpan<byte> packet)
    {
        var version = packet[0];
        if (version == 3)
        {
            // Version, 5, type, 4-byte time, then the 8-byte key ID.
            return new SignatureKey(SignatureKind.OpenPgp, Convert.ToHexString(packet.Slice(7, 8)), KeyIdIsShort: true);
        }

        if (version is not (4 or 5 or 6))
        {
            return new SignatureKey(SignatureKind.OpenPgp, null);
        }

        // Version, type, public-key algorithm, hash algorithm, then the
        // hashed and unhashed subpacket areas, each after its length: two
        // bytes in version 4, four in versions 5 and 6.
        var at = 4;
        string? keyId = null;
        for (var area = 0; area < 2; area++)
        {
            int size;
            if (version == 4)
            {
                size = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(at, 2));
                at += 2;
            }
            else
            {
                size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(at, 4)));
                at += 4;
            }

            var end = at + size;
            while (at < end)
            {
                var length = checked((int)NewLength(packet, ref at, subpacket: true));
                var type = packet[at] & 0x7F;
                var body = packet.Slice(at + 1, length - 1);
                if (type == 33 && body.Length > 1)
                {
                    return new SignatureKey(SignatureKind.OpenPgp, Convert.ToHexString(body[1..]));
                }

                if (type == 16 && body.Length == 8)
                {
                    keyId ??= Convert.ToHexString(body);
                }

                at += length;
            }

            at = end;
        }

        return new SignatureKey(SignatureKind.OpenPgp, keyId, KeyIdIsShort: keyId is not null);
    }

    /// <summary>The public key in an SSH signature (PROTOCOL.sshsig): the magic, a version, then the key as an SSH string.</summary>
    private static SignatureKey SshKey(byte[] data)
    {
        if (data.Length < 14 || Encoding.ASCII.GetString(data, 0, 6) != "SSHSIG")
        {
            return new SignatureKey(SignatureKind.Ssh, null);
        }

        var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(10, 4)));
        var key = data.AsSpan(14, length);
        return new SignatureKey(SignatureKind.Ssh, "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('='));
    }
}
