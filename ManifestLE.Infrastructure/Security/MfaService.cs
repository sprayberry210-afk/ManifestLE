using System;
using System.Security.Cryptography;
using System.Text;
using OtpNet;
using QRCoder;

namespace ManifestLE.Infrastructure.Security;

public class MfaService
{
    private static readonly byte[] ProtectionPurpose =
        Encoding.UTF8.GetBytes("ManifestLE.Development.Mfa.v1");

    public byte[] GenerateSecret()
    {
        return RandomNumberGenerator.GetBytes(20);
    }

    public string GetManualEntryKey(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return Base32Encoding.ToString(secret);
    }

    public byte[] ProtectSecret(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return ProtectedData.Protect(
            secret,
            ProtectionPurpose,
            DataProtectionScope.CurrentUser);
    }

    public byte[] UnprotectSecret(byte[] protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);

        return ProtectedData.Unprotect(
            protectedSecret,
            ProtectionPurpose,
            DataProtectionScope.CurrentUser);
    }

    public byte[] CreateQrCode(
        byte[] secret,
        string username,
        string agencyName)
    {
        ArgumentNullException.ThrowIfNull(secret);

        string issuer = "ManifestLE";
        string account = $"{agencyName} - {username}";
        string label = Uri.EscapeDataString($"{issuer}:{account}");
        string key = GetManualEntryKey(secret);

        string enrollmentUri =
            $"otpauth://totp/{label}" +
            $"?secret={Uri.EscapeDataString(key)}" +
            $"&issuer={Uri.EscapeDataString(issuer)}" +
            "&algorithm=SHA1&digits=6&period=30";

        using var generator = new QRCodeGenerator();

        using var qrData = generator.CreateQrCode(
            enrollmentUri,
            QRCodeGenerator.ECCLevel.Q);

        using var qrCode = new PngByteQRCode(qrData);

        return qrCode.GetGraphic(8);
    }

    public bool VerifyCode(
        byte[] secret,
        string code,
        long? lastAcceptedTimeStep,
        out long matchedTimeStep)
    {
        ArgumentNullException.ThrowIfNull(secret);

        matchedTimeStep = 0;

        if (code is null)
        {
            return false;
        }

        code = code.Trim();

        if (code.Length != 6)
        {
            return false;
        }

        foreach (char character in code)
        {
            if (character < '0' || character > '9')
            {
                return false;
            }
        }

        var totp = new Totp(
            secret,
            step: 30,
            mode: OtpHashMode.Sha1,
            totpSize: 6);

        bool isValid = totp.VerifyTotp(
            code,
            out matchedTimeStep,
            new VerificationWindow(previous: 1, future: 1));

        if (!isValid)
        {
            return false;
        }

        return !lastAcceptedTimeStep.HasValue ||
               matchedTimeStep > lastAcceptedTimeStep.Value;
    }
}