using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.Iyzico.Models;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Iyzico.Controllers
{
    [AuthorizeAdmin]
    [Area(AreaNames.Admin)]
    [AutoValidateAntiforgeryToken]
    public class PaymentIyzicoController : BasePaymentController
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IOrderService _orderService;
        private readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public PaymentIyzicoController(ILocalizationService localizationService,
            INotificationService notificationService,
            IPermissionService permissionService,
            ISettingService settingService,
            IStoreContext storeContext,
            IOrderService orderService,
            IWorkContext workContext)
        {
            _localizationService = localizationService;
            _notificationService = notificationService;
            _permissionService = permissionService;
            _settingService = settingService;
            _storeContext = storeContext;
            _orderService = orderService;
            _workContext = workContext;
        }

        #endregion

        #region Methods
        /// <returns>A task that represents the asynchronous operation</returns>
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var iyzicoPaymentSettings = _settingService.LoadSetting<IyzicoPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                UseToPaymentPopup = iyzicoPaymentSettings.UseToPaymentPopup,
                ApiKey = iyzicoPaymentSettings.ApiKey,
                ApiSecret = iyzicoPaymentSettings.ApiSecret,
                ApiUrl = iyzicoPaymentSettings.ApiUrl,
                PaymentSuccessUrl = iyzicoPaymentSettings.PaymentSuccessUrl,
                PaymentErrorUrl = iyzicoPaymentSettings.PaymentErrorUrl,
                IsCardStorage = iyzicoPaymentSettings.IsCardStorage,
                InstallmentNumber2 = iyzicoPaymentSettings.InstallmentNumber2,
                InstallmentNumber3 = iyzicoPaymentSettings.InstallmentNumber3,
                InstallmentNumber6 = iyzicoPaymentSettings.InstallmentNumber6,
                InstallmentNumber9 = iyzicoPaymentSettings.InstallmentNumber9,
                InstallmentNumber12 = iyzicoPaymentSettings.InstallmentNumber12,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope > 0)
            {
                model.UseToPaymentPopup_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.UseToPaymentPopup, storeScope);
                model.ApiKey_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.ApiKey, storeScope);
                model.ApiSecret_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.ApiSecret, storeScope);
                model.ApiUrl_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.ApiUrl, storeScope);
                model.PaymentSuccessUrl_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.PaymentSuccessUrl, storeScope);
                model.PaymentErrorUrl_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.PaymentErrorUrl, storeScope);
                model.IsCardStorage_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.IsCardStorage, storeScope);
                model.InstallmentNumber2_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.InstallmentNumber2, storeScope);
                model.InstallmentNumber3_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.InstallmentNumber3, storeScope);
                model.InstallmentNumber6_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.InstallmentNumber6, storeScope);
                model.InstallmentNumber9_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.InstallmentNumber9, storeScope);
                model.InstallmentNumber12_OverrideForStore = _settingService.SettingExists(iyzicoPaymentSettings, x => x.InstallmentNumber12, storeScope);
            }

            return View("~/Plugins/Payments.Iyzico/Views/Configure.cshtml", model);
        }

        [HttpPost]
        /// <returns>A task that represents the asynchronous operation</returns>
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //load settings for a chosen store scope
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var iyzicoPaymentSettings = _settingService.LoadSetting<IyzicoPaymentSettings>(storeScope);

            //save settings
            iyzicoPaymentSettings.UseToPaymentPopup = model.UseToPaymentPopup;
            iyzicoPaymentSettings.ApiKey = model.ApiKey;
            iyzicoPaymentSettings.ApiSecret = model.ApiSecret;
            iyzicoPaymentSettings.ApiUrl = model.ApiUrl;
            iyzicoPaymentSettings.PaymentSuccessUrl = model.PaymentSuccessUrl;
            iyzicoPaymentSettings.PaymentErrorUrl = model.PaymentErrorUrl;
            iyzicoPaymentSettings.IsCardStorage = model.IsCardStorage;
            iyzicoPaymentSettings.InstallmentNumber2 = model.InstallmentNumber2;
            iyzicoPaymentSettings.InstallmentNumber3 = model.InstallmentNumber3;
            iyzicoPaymentSettings.InstallmentNumber6 = model.InstallmentNumber6;
            iyzicoPaymentSettings.InstallmentNumber9 = model.InstallmentNumber9;
            iyzicoPaymentSettings.InstallmentNumber12 = model.InstallmentNumber12;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */

            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.UseToPaymentPopup, model.UseToPaymentPopup_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.ApiKey, model.ApiKey_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.ApiSecret, model.ApiSecret_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.ApiUrl, model.ApiUrl_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.PaymentSuccessUrl, model.PaymentSuccessUrl_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.PaymentSuccessUrl, model.PaymentErrorUrl_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.IsCardStorage, model.IsCardStorage_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.InstallmentNumber2, model.InstallmentNumber2_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.InstallmentNumber3, model.InstallmentNumber3_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.InstallmentNumber6, model.InstallmentNumber6_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.InstallmentNumber9, model.InstallmentNumber9_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(iyzicoPaymentSettings, x => x.InstallmentNumber12, model.InstallmentNumber12_OverrideForStore, storeScope, false);

            //now clear settings cache
            _settingService.ClearCache();

            _notificationService.SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        public IActionResult CancelOrder()
        {
            var order = (_orderService.SearchOrders((_storeContext.CurrentStore).Id,
                customerId: (_workContext.CurrentCustomer).Id, pageSize: 1)).FirstOrDefault();

            if (order != null)
                return RedirectToRoute("OrderDetails", new { orderId = order.Id });

            return RedirectToRoute("Homepage");
        }

        #endregion
    }
}