using System.Collections.Generic;
using Newtonsoft.Json;

namespace Audiotica.Data.Model.Spotify.Models
{
    public class SeveralAlbums
    {
        [JsonProperty("albums")]
        public List<FullAlbum> Albums { get; set; }
    }
}