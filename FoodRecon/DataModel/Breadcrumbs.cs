using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace FoodRecon
{
    public class Breadcrumbs
    {
        public string Id { get; set; }

        [JsonProperty(PropertyName = "description")]
        public string Description { get; set; }

        [JsonProperty(PropertyName = "lat")]
        public double Latitude { get; set; }

        [JsonProperty(PropertyName = "lon")]
        public double Longitude { get; set; }

        [JsonProperty(PropertyName = "upvotes")]
        public int UpVotes { get; set; }

        [JsonProperty(PropertyName = "downvotes")]
        public int DownVotes { get; set; }

        [JsonProperty(PropertyName = "starttime")]
        public DateTime StartTime { get; set; }

        [JsonProperty(PropertyName = "age")]
        public String Age { get; set; }
    }
}
