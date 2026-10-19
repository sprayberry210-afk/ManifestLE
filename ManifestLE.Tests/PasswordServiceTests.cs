using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace ManifestLE.Tests;

public class PasswordServiceTests
{
    private readonly PasswordService _service = new();

    [Fact]
    public void CorrectPasswordIsAccepted()
    {
        var user = new AppUser
        {
            Username = "test.officer",
            DisplayName = "Test Officer"
        };

        const string password = "Development test password 123!";

        string hash = _service.HashPassword(user, password);

        var result = _service.VerifyPassword(
            user,
            hash,
            password);

        Assert.NotEqual(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void IncorrectPasswordIsRejected()
    {
        var user = new AppUser
        {
            Username = "test.officer",
            DisplayName = "Test Officer"
        };

        string hash = _service.HashPassword(
            user,
            "Development test password 123!");

        var result = _service.VerifyPassword(
            user,
            hash,
            "Different test password 456!");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void SamePasswordProducesDifferentHashes()
    {
        var user = new AppUser
        {
            Username = "test.officer",
            DisplayName = "Test Officer"
        };

        const string password = "Development test password 123!";

        string firstHash = _service.HashPassword(user, password);
        string secondHash = _service.HashPassword(user, password);

        Assert.NotEqual(firstHash, secondHash);

        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            _service.VerifyPassword(user, firstHash, password));

        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            _service.VerifyPassword(user, secondHash, password));
    }
}