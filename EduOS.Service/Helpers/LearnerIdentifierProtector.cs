using EduOS.Core.Common;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Helpers;

public sealed class LearnerIdentifierProtector : ILearnerIdentifierProtector
{
    private const string ProtectionPurpose = "EduOS.LearnerIdentifier.v1";
    private readonly LearnerIdentitySettings _settings;
    private readonly IDataProtector _protector;

    public LearnerIdentifierProtector(
        IOptions<LearnerIdentitySettings> settings,
        IDataProtectionProvider dataProtectionProvider)
    {
        _settings = settings.Value;
        _protector = dataProtectionProvider.CreateProtector(ProtectionPurpose);
    }

    public bool TryNormalize(
        PersonIdentifierType type,
        string? suppliedValue,
        out string normalizedValue)
    {
        normalizedValue = string.Empty;
        if (string.IsNullOrWhiteSpace(suppliedValue))
            return false;

        var digits = new StringBuilder(suppliedValue.Length);
        foreach (var character in suppliedValue.Trim())
        {
            if (character is >= '0' and <= '9')
            {
                digits.Append(character);
                continue;
            }

            if (character is >= '\u09E6' and <= '\u09EF')
            {
                digits.Append((char)('0' + character - '\u09E6'));
                continue;
            }

            if (char.IsWhiteSpace(character) || character == '-')
                continue;

            return false;
        }

        normalizedValue = digits.ToString();
        return type switch
        {
            PersonIdentifierType.BirthRegistration => normalizedValue.Length == 17,
            PersonIdentifierType.NationalId => normalizedValue.Length is 10 or 13 or 17,
            _ => false
        };
    }

    public string ComputeLookupDigest(
        PersonIdentifierType type,
        string normalizedValue)
    {
        if (!TryNormalize(type, normalizedValue, out var canonical)
            || !string.Equals(canonical, normalizedValue, StringComparison.Ordinal))
        {
            throw new ArgumentException("A normalized identifier is required.", nameof(normalizedValue));
        }

        var key = GetLookupKey();
        try
        {
            using var hmac = new HMACSHA256(key);
            var payload = Encoding.UTF8.GetBytes($"{(int)type}:{canonical}");
            try
            {
                return Convert.ToBase64String(hmac.ComputeHash(payload));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public string Protect(string normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
            throw new ArgumentException("A normalized identifier is required.", nameof(normalizedValue));

        // Validate the independently managed lookup secret even though Data
        // Protection uses its own key ring. Identifier writes fail closed unless
        // both protections are configured.
        var key = GetLookupKey();
        CryptographicOperations.ZeroMemory(key);
        return _protector.Protect(normalizedValue);
    }

    private byte[] GetLookupKey()
    {
        if (string.IsNullOrWhiteSpace(_settings.LookupKeyBase64))
            throw new LearnerIdentityProtectionException("Learner identity protection is not configured.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(_settings.LookupKeyBase64);
        }
        catch (FormatException ex)
        {
            throw new LearnerIdentityProtectionException(
                "Learner identity protection is not configured correctly.", ex);
        }

        if (key.Length < 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new LearnerIdentityProtectionException(
                "Learner identity protection requires at least 32 bytes of key material.");
        }

        return key;
    }
}
