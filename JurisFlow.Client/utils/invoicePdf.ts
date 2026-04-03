export interface InvoicePdfFirm {
  name?: string;
  taxId?: string;
  address?: string;
  city?: string;
  state?: string;
  zipCode?: string;
  phone?: string;
  website?: string;
}

export interface InvoicePdfParty {
  id?: string;
  name?: string;
  email?: string;
  company?: string;
}

export interface InvoicePdfMatter {
  id?: string;
  name?: string;
  caseNumber?: string;
  responsibleAttorney?: string;
}

export interface InvoicePdfLineItem {
  id?: string;
  description?: string;
  date?: string | Date | null;
  serviceDate?: string | Date | null;
  quantity?: number;
  rate?: number;
  amount?: number;
  type?: string;
  taskCode?: string;
  activityCode?: string;
  expenseCode?: string;
}

export interface InvoicePdfData {
  number?: string;
  issueDate?: string | Date | null;
  dueDate?: string | Date | null;
  status?: string;
  subtotal?: number;
  tax?: number;
  taxRate?: number;
  discount?: number;
  amount?: number;
  total?: number;
  amountPaid?: number;
  balance?: number;
  notes?: string;
  terms?: string;
  client?: InvoicePdfParty | null;
  matter?: InvoicePdfMatter | null;
  firm?: InvoicePdfFirm | null;
  lineItems?: InvoicePdfLineItem[];
}

const currencyFormatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD'
});

const dateFormatter = new Intl.DateTimeFormat('en-US', {
  month: '2-digit',
  day: '2-digit',
  year: 'numeric'
});

const normalizeMoney = (value?: number | null) => Number.isFinite(value) ? Number(value) : 0;

const formatCurrency = (value?: number | null) => currencyFormatter.format(normalizeMoney(value));

const formatDate = (value?: string | Date | null) => {
  if (!value) return '';
  const parsed = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(parsed.getTime())) return '';
  return dateFormatter.format(parsed);
};

const buildAddressLine = (firm?: InvoicePdfFirm | null) => {
  if (!firm) return '';
  return [firm.city, firm.state, firm.zipCode].filter(Boolean).join(', ').replace(', ,', ',');
};

const buildCodeLabel = (item: InvoicePdfLineItem) => {
  const codes = [item.taskCode, item.activityCode, item.expenseCode].filter(Boolean);
  return codes.length > 0 ? codes.join(' / ') : '';
};

const resolveServiceDate = (item: InvoicePdfLineItem) => item.date ?? item.serviceDate ?? null;

export const downloadInvoicePdf = async (invoice: InvoicePdfData) => {
  const { jsPDF } = await import('jspdf');
  const doc = new jsPDF({ unit: 'pt', format: 'letter' });
  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const margin = 40;
  const contentWidth = pageWidth - (margin * 2);
  const total = normalizeMoney(invoice.total ?? invoice.amount);
  const amountPaid = normalizeMoney(invoice.amountPaid);
  const balance = typeof invoice.balance === 'number'
    ? normalizeMoney(invoice.balance)
    : Math.max(0, total - amountPaid);
  const subtotal = normalizeMoney(invoice.subtotal);
  const tax = normalizeMoney(invoice.tax);
  const discount = normalizeMoney(invoice.discount);
  const taxLabel = typeof invoice.taxRate === 'number' && invoice.taxRate > 0
    ? `Tax (${invoice.taxRate.toFixed(2).replace(/\.00$/, '')}%)`
    : 'Tax';

  let y = margin;

  const ensureSpace = (height: number) => {
    if (y + height <= pageHeight - margin) return;
    doc.addPage();
    y = margin;
  };

  const writeMuted = (text: string, x: number, ypos: number, align: 'left' | 'right' = 'left') => {
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(107, 114, 128);
    doc.text(text, x, ypos, { align });
  };

  const writeStrong = (text: string, x: number, ypos: number, options?: { size?: number; align?: 'left' | 'right' }) => {
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(options?.size ?? 12);
    doc.setTextColor(15, 23, 42);
    doc.text(text, x, ypos, { align: options?.align ?? 'left' });
  };

  writeStrong(invoice.firm?.name || 'Law Firm', margin, y, { size: 20 });
  y += 18;
  writeMuted('Legal Invoice', margin, y);

  const firmLines = [
    invoice.firm?.address,
    buildAddressLine(invoice.firm),
    invoice.firm?.phone,
    invoice.firm?.website,
    invoice.firm?.taxId ? `Tax ID: ${invoice.firm.taxId}` : ''
  ].filter(Boolean) as string[];

  let firmY = y + 18;
  firmLines.forEach(line => {
    writeMuted(line, margin, firmY);
    firmY += 14;
  });

  const rightX = pageWidth - margin;
  writeStrong('INVOICE', rightX, margin + 4, { size: 22, align: 'right' });
  writeMuted(`Invoice No: ${invoice.number || 'Pending'}`, rightX, margin + 24, 'right');
  writeMuted(`Issue Date: ${formatDate(invoice.issueDate) || 'N/A'}`, rightX, margin + 38, 'right');
  writeMuted(`Due Date: ${formatDate(invoice.dueDate) || 'N/A'}`, rightX, margin + 52, 'right');
  writeMuted(`Status: ${invoice.status || 'Draft'}`, rightX, margin + 66, 'right');

  y = Math.max(firmY, margin + 88);
  ensureSpace(90);

  doc.setDrawColor(226, 232, 240);
  doc.line(margin, y, pageWidth - margin, y);
  y += 22;

  writeStrong('Bill To', margin, y, { size: 12 });
  writeStrong('Matter', margin + (contentWidth / 2), y, { size: 12 });
  y += 18;

  const billToLines = [
    invoice.client?.company || '',
    invoice.client?.name || '',
    invoice.client?.email || ''
  ].filter(Boolean) as string[];

  const matterLines = [
    invoice.matter?.caseNumber ? `Matter No: ${invoice.matter.caseNumber}` : '',
    invoice.matter?.name ? `Matter: ${invoice.matter.name}` : '',
    invoice.matter?.responsibleAttorney ? `Attorney: ${invoice.matter.responsibleAttorney}` : ''
  ].filter(Boolean) as string[];

  const leftBlockStart = y;
  billToLines.forEach((line, idx) => {
    if (idx === 0) {
      writeStrong(line, margin, y, { size: 11 });
    } else {
      writeMuted(line, margin, y);
    }
    y += 14;
  });

  const rightBlockStart = leftBlockStart;
  let rightBlockY = rightBlockStart;
  matterLines.forEach((line, idx) => {
    if (idx === 0) {
      writeStrong(line, margin + (contentWidth / 2), rightBlockY, { size: 11 });
    } else {
      writeMuted(line, margin + (contentWidth / 2), rightBlockY);
    }
    rightBlockY += 14;
  });

  y = Math.max(y, rightBlockY) + 18;
  ensureSpace(40);

  doc.setFillColor(248, 250, 252);
  doc.rect(margin, y, contentWidth, 24, 'F');
  doc.setDrawColor(226, 232, 240);
  doc.rect(margin, y, contentWidth, 24);
  writeStrong('Date', margin + 6, y + 16, { size: 10 });
  writeStrong('Description', margin + 78, y + 16, { size: 10 });
  writeStrong('Qty / Hrs', margin + 350, y + 16, { size: 10 });
  writeStrong('Rate', margin + 432, y + 16, { size: 10 });
  writeStrong('Amount', pageWidth - margin - 6, y + 16, { size: 10, align: 'right' });
  y += 30;

  const lineItems = Array.isArray(invoice.lineItems) ? invoice.lineItems : [];
  if (lineItems.length === 0) {
    ensureSpace(22);
    writeMuted('No line items were recorded for this invoice.', margin + 6, y + 12);
    y += 22;
  } else {
    for (const item of lineItems) {
      const descriptionParts = [item.description || 'Line item'];
      const codeLabel = buildCodeLabel(item);
      if (codeLabel) {
        descriptionParts.push(`Codes: ${codeLabel}`);
      }
      const wrapped = doc.splitTextToSize(descriptionParts.join('\n'), 250);
      const rowHeight = Math.max(22, wrapped.length * 12 + 6);
      ensureSpace(rowHeight + 4);

      doc.setDrawColor(241, 245, 249);
      doc.line(margin, y + rowHeight, pageWidth - margin, y + rowHeight);
      writeMuted(formatDate(resolveServiceDate(item)), margin + 6, y + 14);
      doc.setFont('helvetica', 'normal');
      doc.setFontSize(10);
      doc.setTextColor(31, 41, 55);
      doc.text(wrapped, margin + 78, y + 14);
      writeMuted((item.quantity ?? 0).toFixed(2), margin + 350, y + 14);
      writeMuted(formatCurrency(item.rate), margin + 432, y + 14);
      writeStrong(formatCurrency(item.amount), pageWidth - margin - 6, y + 14, { size: 10, align: 'right' });
      y += rowHeight;
    }
  }

  y += 20;
  ensureSpace(120);

  const summaryX = pageWidth - margin - 190;
  const summaryRows = [
    ['Subtotal', formatCurrency(subtotal)],
    ...(tax > 0 ? [[taxLabel, formatCurrency(tax)] as [string, string]] : []),
    ...(discount > 0 ? [['Discount', `-${formatCurrency(discount)}`] as [string, string]] : []),
    ...(amountPaid > 0 ? [['Payments Received', `-${formatCurrency(amountPaid)}`] as [string, string]] : []),
    ['Balance Due', formatCurrency(balance)]
  ];

  summaryRows.forEach(([label, value], index) => {
    const rowY = y + (index * 18);
    if (label === 'Balance Due') {
      writeStrong(label, summaryX, rowY, { size: 12 });
      writeStrong(value, pageWidth - margin, rowY, { size: 12, align: 'right' });
    } else {
      writeMuted(label, summaryX, rowY);
      writeMuted(value, pageWidth - margin, rowY, 'right');
    }
  });

  y += (summaryRows.length * 18) + 18;
  ensureSpace(90);

  if (invoice.terms) {
    writeStrong('Payment Terms', margin, y, { size: 11 });
    y += 14;
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(71, 85, 105);
    const termsLines = doc.splitTextToSize(invoice.terms, contentWidth);
    doc.text(termsLines, margin, y);
    y += (termsLines.length * 12) + 10;
  }

  if (invoice.notes) {
    ensureSpace(70);
    writeStrong('Client Notes', margin, y, { size: 11 });
    y += 14;
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(71, 85, 105);
    const noteLines = doc.splitTextToSize(invoice.notes, contentWidth);
    doc.text(noteLines, margin, y);
    y += (noteLines.length * 12) + 10;
  }

  const footerY = pageHeight - margin + 4;
  doc.setDrawColor(226, 232, 240);
  doc.line(margin, footerY - 18, pageWidth - margin, footerY - 18);
  writeMuted('This invoice is formatted for standard U.S. legal billing with itemized services, expenses, dates, and balance due.', margin, footerY);

  doc.save(`invoice_${invoice.number || 'draft'}.pdf`);
};
