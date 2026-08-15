using System;
using System.Collections.Specialized;
using WildwoodComponents.WebForms;
using WildwoodComponents.WebForms.Configuration;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    public class OptionsLoaderTests
    {
        private static NameValueCollection Settings(params string[] pairs)
        {
            var settings = new NameValueCollection();
            for (var i = 0; i < pairs.Length; i += 2)
            {
                settings[pairs[i]] = pairs[i + 1];
            }

            return settings;
        }

        [Fact]
        public void Reads_every_setting_under_the_WildwoodAPI_prefix()
        {
            var options = AppSettingsOptionsLoader.Load(Settings(
                "WildwoodAPI:BaseUrl", "https://api.example.test/api",
                "WildwoodAPI:ApiKey", "key-1",
                "WildwoodAPI:AppId", "app-1",
                "WildwoodAPI:AppVersion", "2.5.0",
                "WildwoodAPI:LoginPath", "~/SignIn.aspx",
                "WildwoodAPI:RequestTimeoutSeconds", "45",
                "WildwoodAPI:MaxRetryAttempts", "5",
                "WildwoodAPI:EnableRetry", "false",
                "WildwoodAPI:EnableDetailedErrors", "false",
                "WildwoodAPI:DisableTokenModule", "true"));

            Assert.Equal("https://api.example.test/api", options.BaseUrl);
            Assert.Equal("key-1", options.ApiKey);
            Assert.Equal("app-1", options.AppId);
            Assert.Equal("2.5.0", options.AppVersion);
            Assert.Equal("~/SignIn.aspx", options.LoginPath);
            Assert.Equal(45, options.RequestTimeoutSeconds);
            Assert.Equal(5, options.MaxRetryAttempts);
            Assert.False(options.EnableRetry);
            Assert.False(options.EnableDetailedErrors);
            Assert.True(options.DisableTokenModule);
        }

        [Fact]
        public void Missing_settings_leave_the_defaults_in_place()
        {
            var options = AppSettingsOptionsLoader.Load(new NameValueCollection());

            Assert.Equal(string.Empty, options.BaseUrl);
            Assert.Equal(30, options.RequestTimeoutSeconds);
            Assert.Equal(3, options.MaxRetryAttempts);
            Assert.True(options.EnableRetry);
            Assert.False(options.DisableTokenModule);
        }

        [Fact]
        public void A_malformed_value_is_treated_as_absent_rather_than_fatal()
        {
            // Matches how the Razor package's TryParse-based binder behaves: a typo in
            // web.config should not stop the site starting.
            var options = AppSettingsOptionsLoader.Load(Settings(
                "WildwoodAPI:RequestTimeoutSeconds", "not-a-number",
                "WildwoodAPI:EnableRetry", "maybe"));

            Assert.Equal(30, options.RequestTimeoutSeconds);
            Assert.True(options.EnableRetry);
        }

        [Fact]
        public void Values_are_trimmed()
        {
            var options = AppSettingsOptionsLoader.Load(Settings(
                "WildwoodAPI:BaseUrl", "  https://api.example.test/api  ",
                "WildwoodAPI:AppId", "  app-1  "));

            Assert.Equal("https://api.example.test/api", options.BaseUrl);
            Assert.Equal("app-1", options.AppId);
        }

        [Fact]
        public void A_null_collection_yields_defaults()
        {
            var options = AppSettingsOptionsLoader.Load(null);

            Assert.NotNull(options);
            Assert.Equal(30, options.RequestTimeoutSeconds);
        }

        [Fact]
        public void Keys_without_the_prefix_are_ignored()
        {
            var options = AppSettingsOptionsLoader.Load(Settings("BaseUrl", "https://wrong.example"));

            Assert.Equal(string.Empty, options.BaseUrl);
        }

        // ── Validation ───────────────────────────────────────────────────────────

        [Fact]
        public void Validate_names_the_setting_to_add_when_BaseUrl_is_missing()
        {
            var options = new WildwoodOptions();

            var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

            Assert.Contains("WildwoodAPI:BaseUrl", ex.Message);
        }

        [Theory]
        [InlineData("not-a-url")]
        [InlineData("ftp://api.example.test")]
        [InlineData("/relative/path")]
        public void Validate_requires_an_absolute_http_url(string baseUrl)
        {
            var options = new WildwoodOptions { BaseUrl = baseUrl };

            Assert.Throws<InvalidOperationException>(() => options.Validate());
        }

        [Theory]
        [InlineData("https://api.example.test/api")]
        [InlineData("http://localhost:5000/api")]
        public void Validate_accepts_an_absolute_http_url(string baseUrl)
        {
            var options = new WildwoodOptions { BaseUrl = baseUrl };

            options.Validate();
        }

        [Fact]
        public void Validate_rejects_a_nonsensical_timeout_or_retry_count()
        {
            Assert.Throws<InvalidOperationException>(
                () => new WildwoodOptions { BaseUrl = "https://a.test", RequestTimeoutSeconds = 0 }.Validate());
            Assert.Throws<InvalidOperationException>(
                () => new WildwoodOptions { BaseUrl = "https://a.test", MaxRetryAttempts = 0 }.Validate());
        }
    }
}
