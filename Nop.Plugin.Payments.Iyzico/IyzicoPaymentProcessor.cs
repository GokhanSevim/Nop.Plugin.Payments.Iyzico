using Iyzipay.Model;
using Iyzipay.Request;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.Iyzico.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Tax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nop.Plugin.Payments.Iyzico
{
    /// <summary>
    /// Manual payment processor
    /// </summary>
    public class IyzicoPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly IPaymentService _paymentService;
        private readonly IPaymentIyzicoService _paymentIyzicoService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IyzicoPaymentSettings _iyzicoPaymentSettings;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly ICustomerService _customerService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IAddressService _addressService;
        private readonly IProductService _productService;
        private readonly ITaxService _taxService;
        private readonly ICurrencyService _currencyService;
        private readonly IWorkContext _workContext;
        private readonly ICategoryService _categoryService;

        #endregion

        #region Ctor

        public IyzicoPaymentProcessor(
            ILocalizationService localizationService,
            IPaymentService paymentService,
            IPaymentIyzicoService paymentIyzicoService,
            ISettingService settingService,
            IWebHelper webHelper,
            IHttpContextAccessor httpContextAccessor,
            IyzicoPaymentSettings iyzicoPaymentSettings,
            IShoppingCartService shoppingCartService,
            ICustomerService customerService,
            IOrderTotalCalculationService orderTotalCalculationService,
            IAddressService addressService,
            IProductService productService,
            ITaxService taxService,
            ICurrencyService currencyService,
            IWorkContext workContext,
            ICategoryService categoryService)
        {
            _localizationService = localizationService;
            _paymentService = paymentService;
            _paymentIyzicoService = paymentIyzicoService;
            _settingService = settingService;
            _webHelper = webHelper;
            _httpContextAccessor = httpContextAccessor;
            _iyzicoPaymentSettings = iyzicoPaymentSettings;
            _shoppingCartService = shoppingCartService;
            _customerService = customerService;
            _orderTotalCalculationService = orderTotalCalculationService;
            _addressService = addressService;
            _productService = productService;
            _taxService = taxService;
            _currencyService = currencyService;
            _workContext = workContext;
            _categoryService = categoryService;

            PaymentMethodType = PaymentMethodType.Redirection;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var customer = _customerService.GetCustomerById(processPaymentRequest.CustomerId);
            if (customer == null || customer.Id == 0)
                throw new Exception("Geçerli Bir Müşteri Bulunamadı!");

            var cart = _shoppingCartService.GetShoppingCart(customer: customer, ShoppingCartType.ShoppingCart, processPaymentRequest.StoreId);
            if (!cart.Any())
                throw new Exception("Sepetinizde Ürün Bulunamadı!");

            var billingAddress = _addressService.GetAddressById(customer.BillingAddressId ?? 0);
            if (billingAddress == null)
                throw new NopException("Müşteri fatura adresi ayarlanmadı!");

            var shippingAddress = _addressService.GetAddressById(customer.ShippingAddressId ?? customer.BillingAddressId ?? 0);
            if (shippingAddress == null)
                throw new NopException("Müşteri teslimat adresi ayarlanmadı!");

            var currency = _workContext.WorkingCurrency;

            _orderTotalCalculationService.GetShoppingCartSubTotal(cart, true, out decimal discountAmount, out List<Core.Domain.Discounts.Discount> appliedDiscounts, out decimal subTotalWithoutDiscount, out decimal subTotalWithDiscount, out SortedDictionary<decimal, decimal> taxRates);

            var shoppingCartTotal = _orderTotalCalculationService.GetShoppingCartTotal(cart, true);
            var shoppingCartUnitPriceWithoutDiscount = _currencyService.ConvertFromPrimaryStoreCurrency(subTotalWithDiscount, currency);
            var shoppingCartUnitPriceWithDiscount = _currencyService.ConvertFromPrimaryStoreCurrency(shoppingCartTotal.Value, currency);

            CreateCheckoutFormInitializeRequest request = new CreateCheckoutFormInitializeRequest()
            {
                Locale = Locale.TR.ToString(),
                ConversationId = processPaymentRequest.OrderGuid.ToString(),
                Price = IyzicoHelper.ToDecimalStringInvariant(shoppingCartUnitPriceWithoutDiscount),
                PaidPrice = IyzicoHelper.ToDecimalStringInvariant(shoppingCartUnitPriceWithDiscount),
                Currency = currency.CurrencyCode,
                BasketId = processPaymentRequest.OrderGuid.ToString(),
                PaymentGroup = PaymentGroup.PRODUCT.ToString(),
                CallbackUrl = $"{_webHelper.GetStoreLocation()}PaymentIyzicoPC/PaymentConfirm?orderGuid={processPaymentRequest.OrderGuid}",
                Buyer = _paymentIyzicoService.GetBuyer(processPaymentRequest.CustomerId),
                BasketItems = GetBasketItems(customer, processPaymentRequest.StoreId),
                BillingAddress = _paymentIyzicoService.GetAddress(billingAddress),
                ShippingAddress = _paymentIyzicoService.GetAddress(shippingAddress),
                EnabledInstallments = _iyzicoPaymentSettings.InstallmentNumbers
            };

            if (string.IsNullOrEmpty(request.Buyer.Name))
            {
                request.Buyer.Name = shippingAddress.FirstName;
                request.Buyer.Surname = shippingAddress.LastName;
            }

            if (string.IsNullOrEmpty(request.Buyer.Email))
            {
                request.Buyer.Email = shippingAddress.Email;
            }

            CheckoutFormInitialize payment = CheckoutFormInitialize.Create(request, IyzicoHelper.GetOptions(_iyzicoPaymentSettings));

            var result = new ProcessPaymentResult
            {
                AllowStoringCreditCardNumber = _iyzicoPaymentSettings.IsCardStorage
            };

            if (payment.Status == "failure")
            {
                result.AddError(payment.ErrorMessage);
            }
            else
            {
                result.CaptureTransactionId = payment.ConversationId;
                result.CaptureTransactionResult = payment.Token;

                _httpContextAccessor.HttpContext.Response.Cookies.Append("CurrentShopCartTemp", JsonConvert.SerializeObject(cart));

                _httpContextAccessor.HttpContext.Session.Remove("PaymentPage");

                string PaymentPage;

                if (_iyzicoPaymentSettings.UseToPaymentPopup)
                {
                    PaymentPage = payment.CheckoutFormContent;
                }
                else
                {
                    PaymentPage = payment.PaymentPageUrl;
                }

                _httpContextAccessor.HttpContext.Session.SetString("PaymentPage", PaymentPage);
            }

            return result;
        }

        /// <summary>
        /// Get transaction line items
        /// </summary>
        /// <param name="customer">Customer</param>
        /// <param name="storeId">Store identifier</param>
        /// <returns>List of transaction items</returns>
        private List<BasketItem> GetBasketItems(Core.Domain.Customers.Customer customer, int storeId)
        {
            var items = new List<BasketItem>();

            //get current shopping cart            
            var shoppingCart = _shoppingCartService.GetShoppingCart(customer, ShoppingCartType.ShoppingCart, storeId);

            //define function to create item
            BasketItem createItem(decimal price, string productId, string productName, string categoryName, BasketItemType itemType = BasketItemType.PHYSICAL)
            {
                return new BasketItem
                {
                    Id = productId,
                    Name = productName,
                    Category1 = categoryName,
                    ItemType = itemType.ToString(),
                    Price = IyzicoHelper.ToDecimalStringInvariant(price)
                };
            }

            items.AddRange(shoppingCart.Select(sci =>
            {
                var product = _productService.GetProductById(sci.ProductId);
                var price = IyzicoHelper.ToDecimalInvariant(_shoppingCartService.GetUnitPrice(sci, true));
                var shoppingCartUnitPriceWithDiscountBase = _taxService.GetProductPrice(product, price, true, customer, out decimal taxRate);
                var shoppingCartUnitPriceWithDiscount = _currencyService.ConvertFromPrimaryStoreCurrency(shoppingCartUnitPriceWithDiscountBase, _workContext.WorkingCurrency);

                return createItem(shoppingCartUnitPriceWithDiscount * sci.Quantity,
                    product.Id.ToString(),
                    product.Name,
                    _categoryService.GetProductCategoriesByProductId(sci.ProductId).Aggregate(",", (all, pc) =>
                    {
                        var res = _categoryService.GetCategoryById(pc.CategoryId).Name;
                        res = all == "," ? res : all + ", " + res;
                        return res;
                    }),
                    product.IsShipEnabled ? BasketItemType.PHYSICAL : BasketItemType.VIRTUAL);
            }));

            return items;
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            string PaymentPage = _httpContextAccessor.HttpContext.Session.GetString("PaymentPage");

            if (string.IsNullOrEmpty(PaymentPage) == false)
            {
                _httpContextAccessor.HttpContext.Session.Remove("PaymentPage");

                if (_iyzicoPaymentSettings.UseToPaymentPopup)
                {
                    _httpContextAccessor.HttpContext.Response.WriteAsync(PaymentPage);
                }
                else
                {
                    _httpContextAccessor.HttpContext.Response.Redirect(PaymentPage);
                }
            }
            else
            {
                _httpContextAccessor.HttpContext.Response.Redirect($"{_webHelper.GetStoreLocation()}PaymentIyzicoPC/PaymentConfirm?orderGuid={postProcessPaymentRequest.Order.OrderGuid}");
            }
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the rue - hide; false - display.
        /// </returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the capture payment result
        /// </returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            return new CapturePaymentResult { Errors = new[] { "Capture method not supported" } };
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            return new RefundPaymentResult { Errors = new[] { "Refund method not supported" } };
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            return new VoidPaymentResult { Errors = new[] { "Void method not supported" } };
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { Errors = new[] { "Process Recurring Payment not supported" } };
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the additional handling fee
        /// </returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return _paymentService.CalculateAdditionalFee(cart, 0, false);
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            //always success
            return new CancelRecurringPaymentResult();
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //it's not a redirection payment method. So we always return false
            return false;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of validating errors
        /// </returns>
        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            return new List<string>();
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the payment info holder
        /// </returns>
        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            return new ProcessPaymentRequest();
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentIyzico/Configure";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
        public string GetPublicViewComponentName()
        {
            return "PaymentIyzico";
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override void Install()
        {
            //locales
            _localizationService.AddPluginLocaleResource(new Dictionary<string, string>
            {
                ["Plugins.Payments.Iyzico.Instructions"] = "Iyzico sanal pos entegrasyonunuza ait ayarları düzenleyebilirsiniz.",
                ["Plugins.Payments.Iyzico.Fields.UseToPaymentPopup"] = "Ödeme Sayfası Popup Görünüm",
                ["Plugins.Payments.Iyzico.Fields.UseToPaymentPopup.Hint"] = "Ödeme sayfasının mevcut pencere içerisinde popup olarak açılmasını sağlar.",
                ["Plugins.Payments.Iyzico.PaymentMethodDescription"] = "Kredi/Banka kartı ile ödeme",
                ["Plugins.Payments.Iyzico.AccountInfo"] = "Iyzico Api Bilgilerinizi Tanımlayın",
                ["Plugins.Payments.Iyzico.Fields.ApiKey"] = "Api Key",
                ["Plugins.Payments.Iyzico.Fields.ApiKey.Hint"] = "Iyzico kontrol panelinizde yer alan Api Key bilginizi girin.",
                ["Plugins.Payments.Iyzico.Fields.ApiSecret"] = "Api Secret",
                ["Plugins.Payments.Iyzico.Fields.ApiSecret.Hint"] = "Iyzico kontrol panelinizde yer alan Api Secret bilginizi girin.",
                ["Plugins.Payments.Iyzico.Fields.ApiUrl"] = "Api Url",
                ["Plugins.Payments.Iyzico.Fields.ApiUrl.Hint"] = "Iyzico kontrol panelinizde yer alan Api Url bilginizi girin.",
                ["Plugins.Payments.Iyzico.VirtualPosInfo"] = "Iyzico Ödeme Ayarlarınızı Tanımlayın",
                ["Plugins.Payments.Iyzico.Fields.IsCardStorage"] = "Kart Bilgilerini Sakla",
                ["Plugins.Payments.Iyzico.Fields.IsCardStorage.Hint"] = "Bu seçenek, Iyzico tarafından iletilen kredi kartı bilgilerinin ilk altı hanesini ve son dört hanesini veritabanında saklar (herhangi bir üçüncü taraf işlemciye gönderilmez).",
                ["Plugins.Payments.Iyzico.Fields.PaymentSuccessUrl"] = "Ödeme Başarılı Url",
                ["Plugins.Payments.Iyzico.Fields.PaymentSuccessUrl.Hint"] = "Iyzico sanal pos ödemesi başarılı bir şekilde gerçekleştirildiğinde yönlendirilecek sayfa (url) bilgisinizi tanımlayabilirsiniz. Varsayılan: /checkout/completed/",
                ["Plugins.Payments.Iyzico.Fields.PaymentErrorUrl"] = "Ödeme Başarısız Url",
                ["Plugins.Payments.Iyzico.Fields.PaymentErrorUrl.Hint"] = "Iyzico sanal pos ödemesi başarısız olduğunda yönlendirilecek sayfa (url) bilgisinizi tanımlayabilirsiniz. Varsayılan: /onepagecheckout/",
                ["Plugins.Payments.Iyzico.Fields.InstallmentNumbers"] = "Taksit Seçenekleri",
                ["Plugins.Payments.Iyzico.Fields.InstallmentNumbers.Hint"] = "Iyzico panelinizde taksit seçenekleri aktif ise tarafınıza tanımlı taksit seçeneklerini kullanmak için işaretleyebilirsiniz.",
                ["Plugins.Payments.Iyzico.Fields.InstallmentNumber2"] = "2 Taksit",
                ["Plugins.Payments.Iyzico.Fields.InstallmentNumber3"] = "3 Taksit",
                ["Plugins.Payments.Iyzico.Fields.InstallmentNumber6"] = "6 Taksit",
                ["Plugins.Payments.Iyzico.Fields.InstallmentNumber9"] = "9 Taksit",
                ["Plugins.Payments.Iyzico.Fields.InstallmentNumber12"] = "12 Taksit"
            });

            base.Install();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<IyzicoPaymentSettings>();

            //locales
            _localizationService.DeletePluginLocaleResources("Plugins.Payments.Iyzico");

            base.Uninstall();
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        /// <remarks>
        /// return description of this payment method to be display on "payment method" checkout step. good practice is to make it localizable
        /// for example, for a redirection payment method, description may be like this: "You will be redirected to PayPal site to complete the payment"
        /// </remarks>
        /// <returns>A task that represents the asynchronous operation</returns>
        public string GetPaymentMethodDescription()
        {
            return _localizationService.GetResource("Plugins.Payments.Iyzico.PaymentMethodDescription");
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture => false;

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund => false;

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund => false;

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid => false;

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType { get; set; } = PaymentMethodType.Redirection;

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo => false;

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        /// <remarks>
        /// return description of this payment method to be display on "payment method" checkout step. good practice is to make it localizable
        /// for example, for a redirection payment method, description may be like this: "You will be redirected to PayPal site to complete the payment"
        /// </remarks>
        public string PaymentMethodDescription => _localizationService.GetResource("Plugins.Payments.Iyzico.PaymentMethodDescription");


        #endregion
    }
}