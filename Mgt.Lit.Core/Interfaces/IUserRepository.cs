using Mgt.Lit.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.Interfaces
{
    public interface IUserRepository
    {
        Task<MsUser?> LoginAsync(string username, string password);
    }
}
