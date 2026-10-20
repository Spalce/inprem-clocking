# Phase 4: invoices, offline payments and refunds

[← All test guides](README.md)

Phase 4 lets Inprem invoice organizations for things other than the subscription, for example:
- set-up, training or hardware;
- a balance owed;
- a subscription paid by bank transfer.

It also lets Inprem record money paid offline (bank transfer, cash, cheque, mobile money), give refunds, and check every month that the console agrees with PayPal.

In the console, **Billing** has three tabs: **Subscriptions**, **Invoices** and **Reconciliation**. Tests marked **(sandbox)** need PayPal sandbox keys.

**What counts as an item?** Something Inprem charges an organization for, never clockings.

| Item | Quantity | Price (USD) | When |
|---|---|---|---|
| Setup and onboarding | 1 | 150 | One-time set-up of a new organization |
| Staff training session (hours) | 3 | 40 | Training the organization's admins |
| Kiosk tablet with stand | 2 | 220 | Hardware Inprem supplies |
| Annual subscription, paid by bank transfer | 1 | 290 | An organization that cannot pay through PayPal (choose Offline) |
| Unpaid balance, March to May | 1 | 87 | Collecting money owed |

---

## T4-1 Create and send an offline invoice

**Use case:** Alpha pays its set-up fee and an extra kiosk by mobile money.

1. Console: **Billing** → **Invoices** tab → **+ New invoice**.
2. Choose Alpha in **Organization**, and **Offline (bank transfer, cash, cheque, mobile money)** in **Payment**.
3. Fill in two lines: **Item (up to 200 characters)**, **Quantity**, **Price (USD)**. For example "Setup and training", 1, 50 and "Extra kiosk (months)", 2, 10.
4. Pick a **Due date**, type a **Note to the organization (optional; printed on the invoice)**. Click **Review**.
   - **What it does:** shows the invoice as it will be sent, with the **Total**. Nothing is saved yet; **Back to editing** changes the lines.
5. Click **Create and send**.
   - **What it does:** gives the invoice the next number (e.g. **INV-100001**), saves it as **Sent**, and emails it to Alpha's owners. The owners also get a text, if they agreed to texts.

**Expected:** the invoice page with its number and **Sent**. Alpha's owner receives "Invoice INV-… from Inprem" with the lines and the total (70.00 USD in the example).

## T4-2 Record offline payments

**Use case:** Alpha pays in two parts; the second message about the same payment must not count twice.

1. On the T4-1 invoice, click **Record offline payment**.
2. Set **Date received**, choose **Mobile money** in **Method**, type a **Reference** (e.g. the transaction id) and an **Amount** of 30. Click **Record payment**.
   - **What it does:** adds the payment to the invoice and lowers what is still to pay.
3. Click **Record offline payment** again and use the **same Reference**.
4. Try an **Amount** larger than what is still to pay.
5. Record the rest with a new **Reference**.

**Expected:**
- After step 2: **PartiallyPaid**, 40.00 USD still to pay.
- Step 3 is refused ("already recorded"); step 4 is refused ("more than is still to pay").
- After step 5: **Paid**, and **Cancel invoice** is gone.

## T4-3 Cancel an unpaid invoice

**Use case:** an invoice was sent by mistake.

1. Create another offline invoice as in T4-1.
2. On its page click **Cancel invoice**. Click **Cancel invoice** without a reason; then type a **Reason** and click **Cancel invoice** again.

**Expected:** without a reason it's refused; then **Cancelled**, with the reason shown. **Keep it** goes back without cancelling.

## T4-4 The organization sees its invoices

**Use case:** Alpha's owner checks what they owe and prints a receipt for their accountant.

1. As Alpha's **owner**, add `/BackOffice`, click **Billing**.
2. Look at **Invoices**, then click the number of the paid invoice from T4-2.
3. Click **Print or save as PDF**.
4. As an Alpha admin who is not the owner, look for **Billing**.

**Expected:**
- **Invoices** lists T4-1 to T4-3 with their status, but no drafts and nothing from other organizations.
- An unpaid offline invoice says to pay by bank transfer or mobile money, quoting its number.
- The paid invoice opens as a receipt with its payments, and the print window opens.
- The admin has no **Billing**.

## T4-5 Refund an offline payment

**Use case:** Alpha overpaid; Inprem gives part back.

1. Console: open the paid invoice from T4-2 (**Billing** → **Invoices** → its number).
2. Under **Payments**, click **Refund** on one payment. If asked, type a fresh code.
3. Type an **Amount to refund** smaller than the payment and a **Reason**, click **Refund**, then **OK** in the browser's question.
   - **What it does:** records the refund. For an offline payment nothing is sent anywhere: Inprem gives the money back itself.

**Expected:** **PartiallyRefunded**; the **Refunds** table lists it with your reason; Alpha's **Billing** shows "Paid (… back)".

## T4-6 Only Owners can refund (by default)

1. Sign in to the console as a **Billing** role user (invite one as in T1-9). Open a paid invoice, click **Refund**.

**Expected:** "Only Provider Owners can refund." A refund is refused. A limit for Billing users can be set later (`Billing:RefundLimit`).

## T4-7 Monthly reconciliation report

**Use case:** at the end of each month, Inprem's bookkeeper checks the money in and out.

1. Console: **Billing** → **Reconciliation** tab. Choose this month in **Month**, click **Show**.
   - **What it does:** adds up the month's payments and refunds as the console has them, next to PayPal's figures, and lists every difference.
2. Click **Download CSV**, open it in Excel.

**Expected:**
- T4-2 and T4-5 appear in the **Offline** columns and as **Offline** lines.
- Without PayPal keys, a red message says PayPal is not configured, and only the console's side is shown.
- The CSV has the same lines and a total row.

## T4-8 A PayPal invoice, paid at PayPal (sandbox)

1. Create an invoice as in T4-1, but choose **PayPal invoice (paid online)**.
2. As Alpha's owner, **Billing** → **Pay at PayPal** on that invoice; pay with a sandbox buyer.
3. Wait a minute, refresh the invoice in the console.

**Expected:** PayPal emails the invoice to the owner. After paying: **Paid** with the PayPal payment (at the latest after the next daily PayPal check). The message is under **Webhook events**.

## T4-9 Refund through PayPal (sandbox)

1. On the T4-8 invoice, click **Refund** and refund part of it with a reason.

**Expected:** PayPal returns the money to the sandbox buyer; the invoice shows **PartiallyRefunded**.

## T4-10 Reconciliation equals PayPal (sandbox)

1. After a month with sandbox payments and refunds, open **Reconciliation** for that month.

**Expected:** the totals row says **Equal**, and **Differences** says "None".
