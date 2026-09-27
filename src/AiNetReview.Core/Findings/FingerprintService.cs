namespace AiNetReview.Core.Findings;

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

public sealed class FingerprintService
{
    public string Compute(int fingerprintVersion, string comparisonText)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fingerprintVersion);
        ArgumentNullException.ThrowIfNull(comparisonText);

        var textBytes = Encoding.UTF8.GetBytes(comparisonText);
        var payload = new byte[sizeof(int) + sizeof(int) + textBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, sizeof(int)), fingerprintVersion);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(sizeof(int), sizeof(int)), textBytes.Length);
        textBytes.CopyTo(payload.AsSpan(sizeof(int) * 2));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
