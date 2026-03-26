using Mgt.Lit.WebFront.Services.Member;

namespace Mgt.Lit.WebFront.Services
{
    public class LoggingHeaderHandler : DelegatingHandler
    {
        private readonly PageContext _pageContext;

        public LoggingHeaderHandler(PageContext pageContext)
        {
            _pageContext = pageContext;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_pageContext.Menu))
                request.Headers.Add("X-Menu", _pageContext.Menu);

            if (!string.IsNullOrEmpty(_pageContext.Page))
                request.Headers.Add("X-Page", _pageContext.Page);

            return base.SendAsync(request, cancellationToken);
        }
    }
}
