using Microsoft.Extensions.Configuration;

namespace SamkørselApp.Helper
{
    public class ConnectionStringGetter
    {
        public string GetConnectionString()
        {
            return new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("ConnectionStrings")["DefaultConnection"];
        }
    }
}
