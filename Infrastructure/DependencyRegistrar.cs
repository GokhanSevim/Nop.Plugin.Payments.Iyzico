using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Plugin.Payments.Iyzico.Controllers;
using Nop.Plugin.Payments.Iyzico.Services;

namespace Nop.Plugin.Payments.Iyzico.Infrastructure
{
    public class DependencyRegistrar : IDependencyRegistrar
    {
        public virtual void Register(IServiceCollection services, ITypeFinder typeFinder, AppSettings appSettings)
        {
            services.AddScoped<PaymentIyzicoController>();
            services.AddScoped<IPaymentIyzicoService, PaymentIyzicoService>();
        }

        public int Order => 2;
    }
}
