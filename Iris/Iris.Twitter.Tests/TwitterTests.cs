using System.Collections.Generic;
using System.Threading.Tasks;
using Iris.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Iris.Twitter.Tests
{
    public class FacebookTests
    {
        [Fact]
        public async Task Test()
        {
            var config = JsonExtensions.Read<ProviderConfig>("../../../appsettings.json");

            var facebook = new TwitterProvider(NullLogger<TwitterProvider>.Instance, config);
            IEnumerable<Update> updates = await facebook.GetUpdates(config.WatchedUsers[0]);
            foreach (Update update in updates)
            {
                Assert.NotNull(update?.Id);
                Assert.NotNull(update?.Author?.Id);
            }
        }
    }
}