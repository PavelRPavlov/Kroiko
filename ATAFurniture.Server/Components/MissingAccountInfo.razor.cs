using ATAFurniture.Server.DataAccess;
using ATAFurniture.Server.Models;
using Microsoft.AspNetCore.Components;

namespace ATAFurniture.Server.Components;

public partial class MissingAccountInfo
{
    [Inject] private UserContextService UserContextService { get; set; }
    [CascadingParameter] private ConverterContext ConverterContext { get; set; }
    private bool _isContactInfoComplete;
    protected override void OnInitialized()
    {
        ConverterContext.ContactInfo.CompanyName = UserContextService.User.CompanyName;
        ConverterContext.ContactInfo.MobileNumber = UserContextService.User.MobileNumber;

        _isContactInfoComplete = !string.IsNullOrEmpty(ConverterContext.ContactInfo.CompanyName) &&
                                 !string.IsNullOrEmpty(ConverterContext.ContactInfo.MobileNumber);
    }
}