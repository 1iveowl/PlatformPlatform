namespace SharedKernel.Emails;

public interface IEmailRenderer
{
    Task<EmailRenderResult> RenderEmailAsync(EmailTemplateBase template);
}
