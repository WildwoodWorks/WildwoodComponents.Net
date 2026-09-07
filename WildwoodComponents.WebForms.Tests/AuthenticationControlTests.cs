using WildwoodComponents.WebForms.Controls;
using Xunit;

namespace WildwoodComponents.WebForms.Tests
{
    /// <summary>
    /// The host-owned sign-up URL on the WebForms control, the counterpart of the Razor
    /// component's <c>registerUrl</c>. The control's own registration view must disappear when the
    /// site takes sign-up over, or the site would ship two competing sign-up forms on one page.
    /// </summary>
    public class AuthenticationControlTests
    {
        /// <summary>
        /// The markup's base class is abstract and its resolved-URL members are protected: this
        /// subclass is the same thing the .ascx becomes at runtime, with those members surfaced.
        /// No page or request is involved - a rooted path never reaches <c>ResolveUrl</c>.
        /// </summary>
        private sealed class TestControl : AuthenticationControlBase
        {
            public string RegisterUrlForBrowser
            {
                get { return ResolvedRegisterUrl; }
            }

            public bool RendersRegisterView
            {
                get { return RenderRegisterView; }
            }
        }

        [Fact]
        public void Without_a_register_url_the_control_owns_sign_up()
        {
            var control = new TestControl();

            Assert.Equal(string.Empty, control.RegisterUrlForBrowser);
            Assert.True(control.RendersRegisterView);
        }

        [Fact]
        public void A_rooted_register_url_passes_through_and_suppresses_the_register_view()
        {
            var control = new TestControl { RegisterUrl = "/Account/Signup.aspx" };

            Assert.Equal("/Account/Signup.aspx", control.RegisterUrlForBrowser);
            Assert.False(control.RendersRegisterView);
        }
    }
}
