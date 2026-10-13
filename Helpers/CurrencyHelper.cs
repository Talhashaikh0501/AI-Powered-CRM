using System.Globalization;

namespace AI_Powered_CRM.Helpers
{
    /// <summary>
    /// Centralized INR currency formatting.
    /// Uses the en-IN locale and ISO 4217 code "INR" so values
    /// render as "₹1,00,000.00" in India-standard grouping.
    /// </summary>
    public static class CurrencyHelper
    {
        private static readonly CultureInfo IndianCulture = new CultureInfo("en-IN");
        public const string CurrencyCode = "INR";

        /// <summary>
        /// Formats a decimal value as Indian Rupees, e.g. "₹1,50,000.00".
        /// </summary>
        public static string FormatINR(decimal amount)
            => amount.ToString("C", IndianCulture);

        /// <summary>
        /// Formats with zero decimal places, e.g. "₹1,50,000".
        /// </summary>
        public static string FormatINRRounded(decimal amount)
            => amount.ToString("C0", IndianCulture);

        /// <summary>
        /// Returns the ISO 4217 code: "INR".
        /// </summary>
        public static string IsoCode => CurrencyCode;
    }
}
