using System.Linq;
using System.Threading.Tasks;
using ATAFurniture.Server.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace ATAFurniture.Server.DataAccess;

public class UserContextService
{
    private const string UserIdClaimName = "oid";
    private const string UserNameClaimName = "name";
    private const string EmailClaimName = "emails";
    private const string MobileNumberClaimName = "extension_MobileNumber";
    private const string CompanyNameClaimName = "extension_CompanyName";

    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly ILogger<UserContextService> _logger;
    private readonly IKroikoDataRepository _dataRepository;
    public User User { get; private set; }

    public UserContextService(
        AuthenticationStateProvider authenticationStateProvider,
        IKroikoDataRepository dataRepository,
        ILogger<UserContextService> logger)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _dataRepository = dataRepository;
        _logger = logger;
    }
    
    public async Task AddCredits(int count, bool countResets = false)
    {
        if (countResets)
        {
            User.CreditResets++;
        }
        _logger.LogInformation("Adding {CreditCount} credits to user {Id}", count, User.Id);
        User.CreditsCount += count;
        await _dataRepository.UpdateUser(User);
    }

    public async Task ExtractUserIdentity()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity is { IsAuthenticated: true })
        {
            var aadId = user.Claims.FirstOrDefault(c => c.Type.Equals(UserIdClaimName))?.Value;
            var userName = user.Claims.FirstOrDefault(c => c.Type.Equals(UserNameClaimName))?.Value;
            var userEmail = user.Claims.FirstOrDefault(c => c.Type.Equals(EmailClaimName))?.Value;
            var mobileNumber = user.Claims.FirstOrDefault(c => c.Type.Equals(MobileNumberClaimName))?.Value;
            var companyName = user.Claims.FirstOrDefault(c => c.Type.Equals(CompanyNameClaimName))?.Value;
            
            var dbUser = await _dataRepository.GetUserAsync(aadId) ?? await _dataRepository.CreateUser(aadId, 10);
            
            dbUser = await SyncUserClaimsAsync(dbUser, userName, userEmail, mobileNumber, companyName);
            
            User = dbUser; 
        }
    }

    private async Task<User> SyncUserClaimsAsync(User dbUser, string userName, string userEmail, string mobileNumber, string companyName)
    {
        var shouldUpdate = false;
        if (dbUser.Name != userName)
        {
            _logger.LogInformation("Updating user info for {Id} ==> Property NAME to {UserName}", dbUser.Id, userName);
            dbUser.Name = userName ?? "";
            shouldUpdate = true;
        }
        if (dbUser.Email != userEmail)
        {
            _logger.LogInformation("Updating user info for {Id} ==> Property EMAIL to {UserEmail}", dbUser.Id, userEmail);
            dbUser.Email = userEmail ?? "";
            shouldUpdate = true;
        }
        if (dbUser.MobileNumber != mobileNumber)
        {
            _logger.LogInformation("Updating user info for {Id} ==> Property MOBILE_NUMBER to {UserMobileNumber}", dbUser.Id, mobileNumber);
            dbUser.MobileNumber = mobileNumber ?? "";
            shouldUpdate = true;
        }
        if (dbUser.CompanyName != companyName)
        {
            _logger.LogInformation("Updating user info for {Id} ==> Property COMPANY_NAME to {UserCompanyName}", dbUser.Id, companyName);
            dbUser.CompanyName = companyName ?? "";
            shouldUpdate = true;
        }
        
        if(shouldUpdate)
        {
            await _dataRepository.UpdateUser(dbUser);
        }

        return dbUser;
    }

    public async Task ConsumeSingleCredit()
    {
        await _dataRepository.RemoveCredits(User, 1);
        User.CreditsCount--;
    }

    public async Task UpdateSelectedCompanyAsync(ManufacturerBranch targetCompany)
    {
        await _dataRepository.UpdateSelectedCompany(User, targetCompany);
    }

    public async ValueTask<ManufacturerBranch> GetPreviouslySelectedTargetCompanyAsync()
    {
        var user = await _dataRepository.GetUserAsync(User.AadId);
        return user?.LastSelectedCompany;
    }

    public void SignOut()
    {
        User = null;
    }
}