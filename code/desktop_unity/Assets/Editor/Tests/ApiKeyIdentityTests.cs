using NUnit.Framework;

public class ApiKeyIdentityTests
{
    [Test]
    public void KeyId_OnlyKeepsPrefixAndSuffix()
    {
        string id = ApiKeyIdentity.GetKeyId("sk-1234567890abcdef");

        Assert.AreEqual("sk-1234*****cdef", id);
        StringAssert.DoesNotContain("567890ab", id);
    }

    [Test]
    public void KeyHash_IsStableAndDoesNotExposeKey()
    {
        const string key = "sk-test-secret-value";
        string hash1 = ApiKeyIdentity.GetKeyHash(key);
        string hash2 = ApiKeyIdentity.GetKeyHash(key);

        Assert.AreEqual(hash1, hash2);
        StringAssert.StartsWith("sha256:", hash1);
        StringAssert.DoesNotContain(key, hash1);
    }

    [Test]
    public void LocalSource_HasNoCloudKeyIdentity()
    {
        Assert.AreEqual("local", ApiKeyIdentity.GetKeyIdForSource("local"));
        Assert.AreEqual("", ApiKeyIdentity.GetKeyHashForSource("local"));
        Assert.IsFalse(ApiKeyIdentity.IsGlmSource("chat"));
        Assert.IsTrue(ApiKeyIdentity.IsGlmSource("glm"));
    }
}
