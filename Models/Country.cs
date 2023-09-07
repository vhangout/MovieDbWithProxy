namespace MovieDbWithProxy.Models
{
    public class Country
    {
        public string certification { get; set; }
        public string iso_3166_1 { get; set; }
        public bool primary { get; set; }
        public DateTimeOffset release_date { get; set; }
        public string GetRating() => GetRating(certification, iso_3166_1);
        public static string GetRating(string rating, string iso_3166_1)
        {
            if (string.IsNullOrEmpty(rating))
                return null;
            if (string.Equals(iso_3166_1, "us", StringComparison.OrdinalIgnoreCase))
                return rating;
            if (string.Equals(iso_3166_1, "de", StringComparison.OrdinalIgnoreCase))
                iso_3166_1 = "FSK";
            return iso_3166_1 + "-" + rating;
        }
    }
}
