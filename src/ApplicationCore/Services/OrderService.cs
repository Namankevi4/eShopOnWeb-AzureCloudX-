using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Newtonsoft.Json;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.GetBySpecAsync(basketSpec);

        Guard.Against.NullBasket(basketId, basket);
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);
        await _orderRepository.AddAsync(order);

        var orderReservedItems = order.OrderItems.Select(x => new ReserverOrderItem
        {
            ItemId = x.ItemOrdered.CatalogItemId,
            Quantity = x.Units
        });

        var orderDetail = new OrderDetail();

        orderDetail.TotalPrice = order.Total();
        orderDetail.ShippingAddress = order.ShipToAddress;
        orderDetail.ItemIds = order.OrderItems.Select(x => x.ItemOrdered.CatalogItemId).ToList();

        using var httpClient = new HttpClient();
        HttpRequestMessage newRequest = new HttpRequestMessage(HttpMethod.Post, "https://deliveryorderprocessoraz.azurewebsites.net/api/Function1");
        newRequest.Content = JsonContent.Create(JsonSerializer.Serialize(orderDetail));
        HttpResponseMessage response = await httpClient.SendAsync(newRequest);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Console.WriteLine("order has been successfully reserved");
        }
        else
        {
            Console.WriteLine("something went wrong during reserving order");
        }

        await using var serviceBusClient =
            new ServiceBusClient(
                "Endpoint=sb://finaltaskservicebus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=YqNmh+c11fNODGEzmk41SPm1xf0WSUUW+P1lCzOvbqs=");

        await using ServiceBusSender sender = serviceBusClient.CreateSender("finaltaskservicebusqueue");
        try
        {
            string messageBody = JsonSerializer.Serialize(orderReservedItems);
            var message = new ServiceBusMessage(messageBody);
            Console.WriteLine($"Sending message: {messageBody}");
            await sender.SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now} :: Exception: {ex.Message}");
        }
        finally
        {
            await sender.DisposeAsync();
            await serviceBusClient.DisposeAsync();
        }
    }
}
