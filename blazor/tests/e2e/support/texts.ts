/**
 * The cultures the Blazor edition ships, with the resource texts the specs assert on. The values mirror the en-US and
 * da-DK resources in application/shared-kernel/SharedKernel.Localization; error messages returned by the account API are
 * not in this map because they are shown in English, as returned, in every culture.
 */
export const blazorCultures = [
  {
    locale: "en-US",
    hiWelcomeBack: "Hi! Welcome back",
    createYourAccount: "Create your account",
    enterYourVerificationCode: "Enter your verification code",
    emailAddressRequired: "Email address required",
    emailAddressTooLong: "The email address can be at most 100 characters.",
    verificationCodeFormat: "The verification code must be 6 letters.",
    validForPrefix: "Your verification code is valid for ",
    requestNewCode: "Request a new code",
    newCodeSent: "A new verification code has been sent to your email.",
    setUpYourAccount: "Let's set up your account",
    setUpYourProfile: "Let's set up your profile",
    tenantNameLength: "Name must be between 1 and 30 characters.",
    firstNameLength: "First name must be between 1 and 30 characters.",
    lastNameLength: "Last name must be between 1 and 30 characters.",
    titleTooLong: "Title must be no longer than 50 characters.",
    yourWorkspace: "Your workspace",
    logOut: "Log out",
    logInWithEmail: "Log in with email",
    signUpWithEmail: "Sign up with email",
    verify: "Verify",
    continue: "Continue"
  },
  {
    locale: "da-DK",
    hiWelcomeBack: "Hej! Velkommen tilbage",
    createYourAccount: "Opret din konto",
    enterYourVerificationCode: "Indtast din bekræftelseskode",
    emailAddressRequired: "E-mailadresse påkrævet",
    emailAddressTooLong: "E-mailadressen må højst være på 100 tegn.",
    verificationCodeFormat: "Bekræftelseskoden skal bestå af 6 bogstaver.",
    validForPrefix: "Din bekræftelseskode er gyldig i ",
    requestNewCode: "Anmod om en ny kode",
    newCodeSent: "En ny bekræftelseskode er blevet sendt til din e-mail.",
    setUpYourAccount: "Lad os opsætte din konto",
    setUpYourProfile: "Lad os opsætte din profil",
    tenantNameLength: "Navnet skal være mellem 1 og 30 tegn.",
    firstNameLength: "Fornavnet skal være mellem 1 og 30 tegn.",
    lastNameLength: "Efternavnet skal være mellem 1 og 30 tegn.",
    titleTooLong: "Titlen må højst være på 50 tegn.",
    yourWorkspace: "Dit arbejdsområde",
    logOut: "Log ud",
    logInWithEmail: "Log ind med e-mail",
    signUpWithEmail: "Tilmeld dig med e-mail",
    verify: "Bekræft",
    continue: "Fortsæt"
  }
] as const;

export type BlazorCulture = (typeof blazorCultures)[number];

/**
 * Messages the account API returns; the Blazor pages show them as returned, in English, in every culture
 */
export const accountApiMessages = {
  wrongCode: "The code is wrong or no longer valid.",
  tooManyAttempts: "Too many attempts, please request a new code."
} as const;
