using System.Collections.Generic;

namespace WicsPlatform.Client.Models
{
    public class ODataResponse<T>
    {
        public List<T> Value { get; set; }
        public int Count { get; set; }
    }
}
