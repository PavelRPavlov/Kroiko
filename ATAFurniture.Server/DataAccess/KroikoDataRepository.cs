using System.Threading.Tasks;
using ATAFurniture.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace ATAFurniture.Server.DataAccess;

public class KroikoDataRepository : IKroikoDataRepository
{
    private readonly KroikoDataContext _context;

    public KroikoDataRepository(KroikoDataContext context)
    {
        _context = context;
    }

    public async Task<User> GetUserAsync(string aadId)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.AadId == aadId);
    }

    public async Task<User> CreateUser(string aadId, int credits)
    {
        var user = new User
        {
            AadId = aadId,
            CreditsCount = credits,
            CreditResets = 0,
            LastSelectedCompany = null,
            Name = "",
            Email = "",
            MobileNumber = "",
            CompanyName = ""
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<User> UpdateUser(User dbUser)
    {
        var result = _context.Update(dbUser);
        await _context.SaveChangesAsync();
        return result.Entity;
    }

    public async Task<User> RemoveCredits(User dbUser, int i)
    {
        dbUser.CreditsCount -= i;
        await UpdateUser(dbUser);
        return dbUser;
    }

    public Task<User> UpdateSelectedCompany(User dbUser, ManufacturerBranch targetCompany)
    {
        dbUser.LastSelectedCompany = targetCompany;
        return UpdateUser(dbUser);
    }
}