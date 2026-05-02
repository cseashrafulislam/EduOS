using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EduOS.Service.Helpers
{
    public interface IJwtHelper
    {
        TokenResult GenerateAccessToken(User user, List<string> permissions);
        string GenerateRefreshToken();
        ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
        bool ValidateToken(string token, bool validateLifetime = true);
        Task<bool> IsTokenRevokedAsync(string token);
        Task RevokeTokenAsync(string token, string userId);
        Task RevokeAllUserTokensAsync(string userId);
        TokenValidationResult ValidateTokenWithResult(string token);
        Dictionary<string, object> DecodeToken(string token);
        DateTime GetTokenExpiration(string token);
        bool IsTokenExpired(string token);
    }

    public class JwtSettings
    {
        public string Secret { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int AccessTokenExpiryMinutes { get; set; } = 60;
        public int RefreshTokenExpiryDays { get; set; } = 7;
        public bool ValidateIssuer { get; set; } = true;
        public bool ValidateAudience { get; set; } = true;
        public bool ValidateLifetime { get; set; } = true;
        public int ClockSkewSeconds { get; set; } = 5;
        public string? RefreshTokenSecret { get; set; } // Optional different secret for refresh
    }

    public class User
    {
        public string Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int TenantId { get; set; }
        public string UserType { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? LastPasswordChange { get; set; }
        public string? SecurityStamp { get; set; }
    }

    public class TokenResult
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime AccessTokenExpiry { get; set; }
        public DateTime RefreshTokenExpiry { get; set; }
        public string TokenType { get; set; } = "Bearer";
        public Dictionary<string, object>? CustomClaims { get; set; }
    }

    public class TokenValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public ClaimsPrincipal? Principal { get; set; }
        public SecurityToken? SecurityToken { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
    }

    public class JwtHelper : IJwtHelper
    {
        private readonly JwtSettings _settings;
        private readonly TokenValidationParameters _validationParameters;
        private readonly TokenValidationParameters _validationParametersNoLifetime;
        private readonly JwtSecurityTokenHandler _tokenHandler;
        private readonly HashSet<string> _revokedTokens; // In-memory store (use Redis/DB in production)
        private readonly object _revokedTokensLock = new object();

        public JwtHelper(IOptions<JwtSettings> settings)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

            if (string.IsNullOrEmpty(_settings.Secret) || _settings.Secret.Length < 32)
                throw new ArgumentException("JWT Secret must be at least 32 characters long");

            _tokenHandler = new JwtSecurityTokenHandler();

            // Validation parameters for normal validation
            _validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret)),
                ValidateIssuer = _settings.ValidateIssuer,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = _settings.ValidateAudience,
                ValidAudience = _settings.Audience,
                ValidateLifetime = _settings.ValidateLifetime,
                ClockSkew = TimeSpan.FromSeconds(_settings.ClockSkewSeconds),
                RequireExpirationTime = true,
                RequireSignedTokens = true
            };

            // Validation parameters without lifetime validation (for expired tokens)
            _validationParametersNoLifetime = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret)),
                ValidateIssuer = _settings.ValidateIssuer,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = _settings.ValidateAudience,
                ValidAudience = _settings.Audience,
                ValidateLifetime = false,
                ClockSkew = TimeSpan.FromSeconds(_settings.ClockSkewSeconds),
                RequireExpirationTime = true,
                RequireSignedTokens = true
            };

            _revokedTokens = new HashSet<string>();
        }

        public TokenResult GenerateAccessToken(User user, List<string> permissions)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrEmpty(user.Email)) throw new ArgumentException("User email is required");

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("tenantId", user.TenantId.ToString()),
                new Claim("userType", user.UserType ?? "User"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };

            // Add security stamp if exists (for password change invalidation)
            if (!string.IsNullOrEmpty(user.SecurityStamp))
            {
                claims.Add(new Claim("security_stamp", user.SecurityStamp));
            }

            // Add custom claims
            claims.Add(new Claim("is_active", user.IsActive.ToString()));

            if (user.LastPasswordChange.HasValue)
            {
                claims.Add(new Claim("last_password_change",
                    user.LastPasswordChange.Value.ToString("o")));
            }

            // Add permissions as claims
            foreach (var permission in permissions.Distinct())
            {
                claims.Add(new Claim("permission", permission));
            }

            // Add role claims if available (you can populate these separately)
            // claims.Add(new Claim(ClaimTypes.Role, role));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expires = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: expires,
                notBefore: DateTime.UtcNow,
                signingCredentials: credentials);

            var accessToken = _tokenHandler.WriteToken(token);

            var refreshToken = GenerateRefreshToken();

            var refreshExpiry = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpiryDays);

            // Add claims to token result
            var customClaims = new Dictionary<string, object>
            {
                ["userId"] = user.Id,
                ["tenantId"] = user.TenantId,
                ["email"] = user.Email,
                ["permissions"] = permissions,
                ["token_id"] = token.Id
            };

            return new TokenResult
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpiry = expires,
                RefreshTokenExpiry = refreshExpiry,
                CustomClaims = customClaims
            };
        }

        public string GenerateRefreshToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);

            // Add timestamp to make it unique
            var timestamp = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
            var combined = new byte[randomNumber.Length + timestamp.Length];
            Buffer.BlockCopy(randomNumber, 0, combined, 0, randomNumber.Length);
            Buffer.BlockCopy(timestamp, 0, combined, randomNumber.Length, timestamp.Length);

            return Convert.ToBase64String(combined);
        }

        public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
        {
            try
            {
                var principal = _tokenHandler.ValidateToken(
                    token,
                    _validationParametersNoLifetime,
                    out var securityToken);

                if (securityToken is not JwtSecurityToken jwtSecurityToken)
                    return null;

                // Check if the token has the correct algorithm
                if (!jwtSecurityToken.Header.Alg.Equals(
                    SecurityAlgorithms.HmacSha256,
                    StringComparison.InvariantCultureIgnoreCase))
                    return null;

                return principal;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool ValidateToken(string token, bool validateLifetime = true)
        {
            try
            {
                var parameters = validateLifetime ? _validationParameters : _validationParametersNoLifetime;
                _tokenHandler.ValidateToken(token, parameters, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public TokenValidationResult ValidateTokenWithResult(string token)
        {
            var result = new TokenValidationResult();

            try
            {
                var principal = _tokenHandler.ValidateToken(
                    token,
                    _validationParameters,
                    out var securityToken);

                result.IsValid = true;
                result.Principal = principal;
                result.SecurityToken = securityToken;

                if (securityToken is JwtSecurityToken jwtToken)
                {
                    result.ValidFrom = jwtToken.ValidFrom;
                    result.ValidTo = jwtToken.ValidTo;
                }

                // Check if token is revoked
                if (IsTokenRevokedAsync(token).GetAwaiter().GetResult())
                {
                    result.IsValid = false;
                    result.ErrorMessage = "Token has been revoked";
                }

                // Check security stamp (if password changed after token was issued)
                var securityStampClaim = principal?.FindFirst("security_stamp")?.Value;
                // Compare with current user's security stamp from database
                // If mismatch, token is invalid
            }
            catch (SecurityTokenExpiredException ex)
            {
                result.IsValid = false;
                result.ErrorMessage = "Token has expired";
            }
            catch (SecurityTokenInvalidSignatureException ex)
            {
                result.IsValid = false;
                result.ErrorMessage = "Invalid token signature";
            }
            catch (SecurityTokenInvalidIssuerException ex)
            {
                result.IsValid = false;
                result.ErrorMessage = "Invalid token issuer";
            }
            catch (SecurityTokenInvalidAudienceException ex)
            {
                result.IsValid = false;
                result.ErrorMessage = "Invalid token audience";
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Token validation failed: {ex.Message}";
            }

            return result;
        }

        public async Task<bool> IsTokenRevokedAsync(string token)
        {
            // In production, check against database or Redis
            // This is an in-memory implementation for demonstration
            await Task.CompletedTask;

            lock (_revokedTokensLock)
            {
                return _revokedTokens.Contains(token);
            }
        }

        public async Task RevokeTokenAsync(string token, string userId)
        {
            await Task.CompletedTask;

            lock (_revokedTokensLock)
            {
                _revokedTokens.Add(token);

                // Clean up old revoked tokens periodically (not shown here)
                // Consider using Redis with TTL or database with expiry
            }

            // In production, store in database:
            // await _dbContext.RevokedTokens.AddAsync(new RevokedToken 
            // { 
            //     Token = token, 
            //     UserId = userId, 
            //     RevokedAt = DateTime.UtcNow,
            //     ExpiresAt = GetTokenExpiration(token) 
            // });
            // await _dbContext.SaveChangesAsync();
        }

        public async Task RevokeAllUserTokensAsync(string userId)
        {
            await Task.CompletedTask;

            // In production, revoke all tokens for a user from database
            // var userTokens = await _dbContext.RevokedTokens
            //     .Where(t => t.UserId == userId && t.ExpiresAt > DateTime.UtcNow)
            //     .ToListAsync();
            // 
            // foreach (var token in userTokens)
            // {
            //     token.RevokedAt = DateTime.UtcNow;
            // }
            // await _dbContext.SaveChangesAsync();
        }

        public Dictionary<string, object> DecodeToken(string token)
        {
            var decoded = new Dictionary<string, object>();

            try
            {
                var jwtToken = _tokenHandler.ReadJwtToken(token);

                foreach (var claim in jwtToken.Claims)
                {
                    if (!decoded.ContainsKey(claim.Type))
                    {
                        decoded.Add(claim.Type, claim.Value);
                    }
                }

                decoded.Add("valid_from", jwtToken.ValidFrom);
                decoded.Add("valid_to", jwtToken.ValidTo);
                decoded.Add("algorithm", jwtToken.Header.Alg);
                decoded.Add("token_type", jwtToken.Header.Typ);
            }
            catch (Exception ex)
            {
                decoded.Add("error", $"Failed to decode token: {ex.Message}");
            }

            return decoded;
        }

        public DateTime GetTokenExpiration(string token)
        {
            try
            {
                var jwtToken = _tokenHandler.ReadJwtToken(token);
                return jwtToken.ValidTo;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public bool IsTokenExpired(string token)
        {
            var expiry = GetTokenExpiration(token);
            return expiry < DateTime.UtcNow;
        }

        // Helper method to refresh tokens with rotation
        public async Task<TokenResult> RefreshTokenAsync(
            string expiredToken,
            string refreshToken,
            Func<string, Task<User?>> getUserByRefreshTokenAsync,
            Func<string, Task<List<string>>> getUserPermissionsAsync)
        {
            // Get principal from expired token
            var principal = GetPrincipalFromExpiredToken(expiredToken);
            if (principal == null)
                throw new SecurityTokenException("Invalid token");

            // Get user ID from principal
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                throw new SecurityTokenException("Invalid user ID in token");

            // Validate refresh token (check against database)
            var user = await getUserByRefreshTokenAsync(refreshToken);
            if (user == null || user.Id != userId)
                throw new SecurityTokenException("Invalid refresh token");

            // Revoke old refresh token (token rotation)
            // await RevokeRefreshTokenAsync(refreshToken);

            // Get user permissions
            var permissions = await getUserPermissionsAsync(userId.ToString());

            // Generate new tokens
            var newTokens = GenerateAccessToken(user, permissions);

            // Revoke old access token
            await RevokeTokenAsync(expiredToken, userId.ToString());

            return newTokens;
        }
    }

    // Extension methods for ClaimsPrincipal
    public static class ClaimsPrincipalExtensions
    {
        public static int? GetUserId(this ClaimsPrincipal principal)
        {
            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
                return userId;
            return null;
        }

        public static int? GetTenantId(this ClaimsPrincipal principal)
        {
            var tenantIdClaim = principal.FindFirst("tenantId")?.Value;
            if (int.TryParse(tenantIdClaim, out var tenantId))
                return tenantId;
            return null;
        }

        public static string? GetEmail(this ClaimsPrincipal principal)
        {
            return principal.FindFirst(ClaimTypes.Email)?.Value;
        }

        public static List<string> GetPermissions(this ClaimsPrincipal principal)
        {
            return principal.FindAll("permission")
                .Select(c => c.Value)
                .ToList();
        }

        public static bool HasPermission(this ClaimsPrincipal principal, string permission)
        {
            return principal.HasClaim("permission", permission);
        }

        public static bool HasAnyPermission(this ClaimsPrincipal principal, params string[] permissions)
        {
            return permissions.Any(p => principal.HasClaim("permission", p));
        }

        public static bool HasAllPermissions(this ClaimsPrincipal principal, params string[] permissions)
        {
            return permissions.All(p => principal.HasClaim("permission", p));
        }
    }
}