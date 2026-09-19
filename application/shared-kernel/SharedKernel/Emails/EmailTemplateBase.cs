namespace SharedKernel.Emails;

// One transactional email: the Razor component that renders its HTML, the model that component binds to, and the plain
// text twin every email carries beside its HTML body. The renderer sets the culture from Locale before it calls either,
// so both read the same localized resources.
public abstract record EmailTemplateBase(string Locale)
{
    public abstract Type ComponentType { get; }

    public abstract object Model { get; }

    public abstract string RenderPlainText(EmailBrand brand);
}
