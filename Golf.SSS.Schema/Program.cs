using System.Threading.Tasks;
using SqlStreamStore;

namespace Golf.SSS.Schema
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var settings = new MsSqlStreamStoreV3Settings("Server=.;Database=golf;User ID=sa;Password=P@ssw0rd");
            var store = new MsSqlStreamStoreV3(settings);
            await store.CreateSchemaIfNotExists();
        }
    }
}