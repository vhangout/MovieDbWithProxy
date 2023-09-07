namespace MovieDbWithProxy.Models
{
    internal class CompleteMovieData
    {
        public bool adult { get; set; }
        public string backdrop_path { get; set; }
        public BelongsToCollection belongs_to_collection { get; set; }
        public int budget { get; set; }
        public List<TmdbGenre> genres { get; set; }
        public string homepage { get; set; }
        public int id { get; set; }
        public string imdb_id { get; set; }
        public string original_language { get; set; }
        public string original_title { get; set; }
        public string overview { get; set; }
        public double popularity { get; set; }
        public string poster_path { get; set; }
        public List<ProductionCompany> production_companies { get; set; }
        public List<ProductionCountry> production_countries { get; set; }
        public string release_date { get; set; }
        public int revenue { get; set; }
        public int runtime { get; set; }
        public List<SpokenLanguage> spoken_languages { get; set; }
        public string status { get; set; }
        public string tagline { get; set; }
        public string title { get; set; }
        public bool video { get; set; }
        public double vote_average { get; set; }
        public int vote_count { get; set; }
        public Casts casts { get; set; }
        public Releases releases { get; set; }
        public Images images { get; set; }
        public TmdbKeywords keywords { get; set; }
        public Trailers trailers { get; set; }
        public string name { get; set; }
        public string original_name { get; set; }
        public string GetOriginalTitle() => this.original_name ?? this.original_title;
        public string GetTitle() => this.name ?? this.title ?? this.GetOriginalTitle();
    }
}
