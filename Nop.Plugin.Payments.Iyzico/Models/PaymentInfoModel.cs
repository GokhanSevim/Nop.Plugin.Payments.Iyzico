using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.Iyzico.Models
{
    public class PaymentInfoModel : BaseNopModel
    {
        public bool UseToPaymentPopup { get; set; }
    }
}