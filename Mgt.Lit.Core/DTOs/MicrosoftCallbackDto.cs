using System;
using System.Collections.Generic;
using System.Text;

namespace Mgt.Lit.Core.DTOs
{
    public class MicrosoftCallbackDto
    {
        public string Code { get; set; } = "";
    }

    public class MicrosoftTokenResponse
    {
        public string AccessToken { get; set; } = "";
        public string IdToken { get; set; } = "";
    }

    public class MicrosoftUserInfo
    {
        public string Mail { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
    }
}
