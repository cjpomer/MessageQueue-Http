using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Threading.Tasks;

namespace Anon.OnPremUploadDownload.AspNetCore.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class AuthorizeEnterpriseIamAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var httpContext = context.HttpContext;
            var user = httpContext.User;

            // check if user is authenticated
            if (user.Identity.IsAuthenticated)
            {
                //TODO: your enterprise IAM
                await Task.CompletedTask;
                var authorized = true;

                if (!authorized)
                {
                    context.Result = new ForbidResult();
                }
                //else do nothing
            }
            else // set 401 if not authenticated
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
