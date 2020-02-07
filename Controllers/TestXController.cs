using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace MockChannel.Controllers
{
    public class TestXController : ApiController
    {
        [HttpGet]
        public string CompleteAll()
        {
            return "working";
        }

        // GET: api/TestX
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET: api/TestX/5
        public string Get(int id)
        {
            return "value";
        }

        // POST: api/TestX
        public void Post([FromBody]string value)
        {
        }

        // PUT: api/TestX/5
        public void Put(int id, [FromBody]string value)
        {
        }

        // DELETE: api/TestX/5
        public void Delete(int id)
        {
        }
    }
}
