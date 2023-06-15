using Iyzipay.Model;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.Iyzico.Services
{
    public interface IPaymentIyzicoService
    {
        Buyer GetBuyer(int customerId);
        Address GetAddress(Core.Domain.Common.Address billingAddress);
    }
}