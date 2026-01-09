using System;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using SamkørselApp.Helper;

namespace SamkørselApp.Helper
{
    public class CurrencyRateHelper : Controller
    {
        private const string CurrencyRatesUrl = "https://www.nationalbanken.dk/api/currencyratesxml?lang=da";
        record CurrencyRate(string code, string desc, string rate);
        public ConnectionStringGetter connectionStringGetter { get; set; }
        public string connectionString { get; set; }
        public CurrencyRateHelper()
        {
            this.connectionStringGetter = new ConnectionStringGetter();
            connectionString = connectionStringGetter.GetConnectionString();
        }

        /// <summary>
        /// Gets the exchange rate for a given currency code (e.g., "USD", "EUR").
        /// </summary>
        /// <param name="currencyCode">The 3-letter currency code.</param>
        /// <returns>The exchange rate as a decimal, or null if not found.</returns>
        public async Task<bool> RefreshExchangeRateAsync()
        {
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(CurrencyRatesUrl);

                if (!response.IsSuccessStatusCode)
                    return false;

                var xmlString = await response.Content.ReadAsStringAsync();

                var doc = XDocument.Parse(xmlString);
                var ns = doc.Root.GetDefaultNamespace();
                var rates = doc.Descendants("currency")
                    .Select(e => new CurrencyRate(
                        (string)e.Attribute("code"),
                        (string)e.Attribute("desc"),
                        (string)e.Attribute("rate")
                    ))
                    .ToList();

                string SqlQuery = "UPDATE [ucollect].[dbo].[CurrencyConverter] SET rate = CASE code";

                foreach (var rate in rates)
                {
                    SqlQuery += $" WHEN '{rate.code}' THEN {rate.rate.Replace(",", ".")}";
                }
                SqlQuery += " END , lastupdate = GETDATE() WHERE code IN (" + string.Join(", ", rates.Select(r => $"'{r.code}'")) + ")";

                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new SqlCommand(SqlQuery, connection))
                    {
                        int rowsAffected = command.ExecuteNonQuery();
                        if (rowsAffected == 31)
                        {
                            return true;
                        }
                    }
                }


                return false;
            }
        }
        
        public bool IsCurrencyRefreshedNeededToday()
        {
            string Query = "SELECT COUNT(*) FROM CurrencyConverter WHERE CAST(lastupdate AS DATE) = CAST(GETDATE() AS DATE)";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(Query, connection))
                {
                    int count = (int)command.ExecuteScalar();
                    return count == 0;
                }
            }
        }

        public double ValueInDKK(double amount, string fromCurrency)
        {
            if (fromCurrency == "DKK") return amount;
            double rate = 1.0;
            string Query = $"SELECT rate FROM CurrencyConverter WHERE code = '{fromCurrency}'";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(Query, connection))
                {
                    var result = command.ExecuteScalar();
                    if (result != null)
                    {
                        rate = Convert.ToDouble(result);
                    }

                }
            }

            return amount / 100 * rate;
        }
    }
}