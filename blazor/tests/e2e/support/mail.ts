/// <reference types="node" />
import { expect } from "@playwright/test";
import { existsSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";

/**
 * A mail the account API sent, as Mailpit received it, with the one-time password it carries
 */
export interface ReceivedMail {
  subject: string;
  html: string;
  text: string;
  oneTimePassword: string;
}

/**
 * How long a mail may take to reach Mailpit, and how often Mailpit is asked in the meantime
 */
const mailTimeoutMs = 30_000;
const mailPollIntervalMs = 250;

/**
 * The offset of MailpitHttp from the base port in application/shared-kernel/SharedKernel/Configuration/PortAllocation.cs
 */
const mailpitHttpPortOffset = 5;

/**
 * The Mailpit HTTP API of this worktree's Aspire stack, on the base port in .workspace/port.txt plus the Mailpit offset
 */
function mailpitBaseUrl(): string {
  const portFile = join(resolve(__dirname, "..", "..", "..", ".."), ".workspace", "port.txt");
  if (!existsSync(portFile)) throw new Error("The Blazor specifications read mail through Mailpit and need .workspace/port.txt.");
  return `http://localhost:${Number.parseInt(readFileSync(portFile, "utf8").trim(), 10) + mailpitHttpPortOffset}`;
}

/**
 * The time to pass to readMailSentAfter, taken just before the action that sends the mail. Mailpit stamps a mail when it
 * receives it on the same machine, so one second of slack covers the rounding of that stamp.
 */
export function mailClock(): number {
  return Date.now() - 1_000;
}

/**
 * Read the newest mail to a recipient that Mailpit received after a point in time, waiting until it arrives. Every mail the
 * account API sends for a one-time password ends with the domain and "#" followed by the code, the suffix browsers use for
 * autofill; the code is taken from there, the way a user reads it, instead of any debug-only shortcut.
 * @param recipient The email address the mail was sent to
 * @param sentAfter The value of mailClock() taken before the action that sends the mail
 */
export async function readMailSentAfter(recipient: string, sentAfter: number): Promise<ReceivedMail> {
  const baseUrl = mailpitBaseUrl();
  const deadline = Date.now() + mailTimeoutMs;
  while (Date.now() < deadline) {
    const search = (await (await fetch(`${baseUrl}/api/v1/search?query=${encodeURIComponent(`to:"${recipient}"`)}`)).json()) as {
      messages: { ID: string; Created: string }[];
    };
    const newest = search.messages
      .filter((message) => new Date(message.Created).getTime() >= sentAfter)
      .sort((first, second) => new Date(second.Created).getTime() - new Date(first.Created).getTime())[0];
    if (newest !== undefined) {
      const message = (await (await fetch(`${baseUrl}/api/v1/message/${newest.ID}`)).json()) as { Subject: string; HTML: string; Text: string };
      const oneTimePassword = message.Text.trim().split("#").at(-1)!.trim();
      return { subject: message.Subject, html: message.HTML, text: message.Text, oneTimePassword };
    }
    await new Promise((resolveDelay) => setTimeout(resolveDelay, mailPollIntervalMs));
  }
  throw new Error(`No mail to ${recipient} reached Mailpit within ${mailTimeoutMs} ms.`);
}

/**
 * Expect a mail to carry the subject and every text in both its HTML and plain text bodies
 * @param mail The mail read with readMailSentAfter
 * @param subject The expected subject
 * @param texts Texts both bodies must contain
 */
export function expectMailContent(mail: ReceivedMail, subject: string, texts: readonly string[]): void {
  expect(mail.subject).toBe(subject);
  for (const text of texts) {
    expect(mail.html).toContain(text);
    expect(mail.text).toContain(text);
  }
}
