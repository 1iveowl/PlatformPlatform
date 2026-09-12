---
paths: **/*Dialog*.tsx,**/*Modal*.tsx,**/*Confirmation*.tsx
description: Rules for dialog structure, DirtyDialog, dialog forms, and dialog sizing
---

# Dialogs

Rules for building dialogs with `DirtyDialog`, a wrapper and body split, and `DialogForm`.

## Implementation

1. Dialog structure and DirtyDialog patterns:
   - **Dialog sizing**: Use `sm:w-dialog-*` utilities - never use custom widths like `max-w-lg` or arbitrary values
   - Split every dialog into two components in the same file: wrapper owns only `isOpen` + `DirtyDialog`/`DialogContent`/`DialogHeader` shell, body lives inside `<DialogContent>` and owns all state/mutation/form. Body unmounts on close, so state auto-resets on reopen — no `handleCloseComplete`, no `mutation.reset()`
   - Body signals dirtiness with `useDialogSetDirty()` from `@repo/ui/components/DirtyDialogContext`. `DirtyDialog` tracks the flag internally and clears it on close
   - `DialogBody` between `DialogHeader` and `DialogFooter` (scroll container)
   - In dialogs use `DialogForm` (not `Form`); a `<Form>` between `DialogContent` and `DialogBody` breaks the scroll chain
   - Cancel button: `<DialogClose render={<Button type="reset" ... />}>` — `type="reset"` skips the unsaved-changes warning
   - Close on success: call `onClose` from the wrapper. Do not reset anything by hand — unmount does it

## Examples

```tsx
// ✅ DO: Wrapper + body split; body unmounts on close so state auto-resets
export function InviteUserDialog({ isOpen, onOpenChange }: InviteUserDialogProps) {
  const handleClose = () => onOpenChange(false);
  return (
    <DirtyDialog open={isOpen} onOpenChange={onOpenChange} trackingTitle="Invite user">
      <DialogContent className="sm:w-dialog-md"> // ✅ Dialog width classes, not arbitrary values
        <DialogHeader>
          <DialogTitle><Trans>Invite user</Trans></DialogTitle>
        </DialogHeader>
        <InviteUserDialogBody onClose={handleClose} />
      </DialogContent>
    </DirtyDialog>
  );
}

function InviteUserDialogBody({ onClose }: { onClose: () => void }) { // ✅ Body owns all state/mutation
  const setDirty = useDialogSetDirty();
  const inviteMutation = api.useMutation("post", "/api/account/users/invite", {
    onSuccess: () => { // ✅ Toast in onSuccess (not useEffect)
      toast.success(t`Success`, { description: t`User invited` });
      onClose(); // ✅ Body unmounts → state gone naturally, no reset code
    }
  });

  return (
    <DialogForm // ✅ DialogForm (not Form) preserves the DialogBody scroll chain
      onSubmit={mutationSubmitter(inviteMutation)}
      validationErrors={inviteMutation.error?.errors}
    >
      <DialogBody> // ✅ Always wrap content in DialogBody
        <TextField autoFocus required name="email" label={t`Email`} onChange={() => setDirty(true)} />
      </DialogBody>
      <DialogFooter>
        <DialogClose render={<Button type="reset" variant="secondary" disabled={inviteMutation.isPending} />}> // ✅ type="reset" bypasses warning; disabled (not isPending) — no spinner on Cancel
          <Trans>Cancel</Trans>
        </DialogClose>
        <Button type="submit" isPending={inviteMutation.isPending}> // ✅ isPending auto-disables and prepends <Spinner />
          {inviteMutation.isPending ? <Trans>Sending...</Trans> : <Trans>Send invite</Trans>}
        </Button>
      </DialogFooter>
    </DialogForm>
  );
}

// ❌ DON'T: State in the wrapper, manual reset plumbing, removed DirtyDialog props
function BadInviteUserDialog({ isOpen, onOpenChange }) {
  const [isFormDirty, setIsFormDirty] = useState(false); // ❌ State in wrapper → persists across close/reopen
  const inviteMutation = api.useMutation("post", "/api/users/invite"); // ❌ Mutation in wrapper → stale errors on reopen

  useEffect(() => { // ❌ useEffect watching isSuccess causes toast timing issues
    if (inviteMutation.isSuccess) toast.success("Success");
  }, [inviteMutation.isSuccess]);

  const handleCloseComplete = () => { // ❌ Manual reset plumbing — symptom of wrong state location
    setIsFormDirty(false);
    inviteMutation.reset();
  };

  return (
    <DirtyDialog open={isOpen} onOpenChange={onOpenChange}
                 hasUnsavedChanges={isFormDirty} onCloseComplete={handleCloseComplete}> // ❌ Not DirtyDialog props; body owns dirtiness via `useDialogSetDirty()`
      <DialogContent className="sm:max-w-lg bg-white"> // ❌ max-w-lg (use w-dialog-md), hardcoded colors
        <h2>User Mgmt</h2> // ❌ Use DialogTitle (not h2), acronym "Mgmt", missing <Trans>
        // ❌ Missing DialogBody → content won't scroll properly
        <TextField name="email" onChange={() => setIsFormDirty(true)} />
        <DialogFooter>
          <DialogClose render={<Button variant="secondary" />}> // ❌ Missing type="reset"
            Cancel
          </DialogClose>
          <Button type="submit"> // ❌ Missing disabled={isPending}
            <Trans>Submit</Trans> // ❌ Missing isPending branch, generic text
          </Button>
        </DialogFooter>
      </DialogContent>
    </DirtyDialog>
  );
}
```
