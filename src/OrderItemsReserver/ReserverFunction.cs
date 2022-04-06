using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace OrderItemsReserver
{
    public static class ReserverFunction
    {
        [FunctionName("ReserverFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [Blob("reserved-order-storage/{rand-guid}.json", FileAccess.Write, Connection = "AzureWebJobsStorage")] Stream outputBlob,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (string.IsNullOrEmpty(requestBody))
            {
                return new BadRequestObjectResult("Cannot find order items");
            }

            try
            {
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                await outputBlob.WriteAsync(Encoding.Default.GetBytes(data));
            }
            catch (Exception e)
            {
                log.LogError(e.Message);
                return new InternalServerErrorResult();
            }

            return new OkObjectResult("order items has been successfully reserved");
        }
    }
}
