import { parseConditionalLogic, parseFieldOptions } from './intakeConditionalLogic';

export interface IntakePdfField {
  id?: string;
  name?: string;
  label?: string;
  type?: string;
  required?: boolean;
  placeholder?: string;
  helpText?: string;
  options?: string;
  defaultValue?: string;
  validationPattern?: string;
  validationMessage?: string;
  conditionalLogic?: string;
  order?: number;
}

export interface IntakeFormPdfData {
  id?: string;
  name?: string;
  description?: string;
  practiceArea?: string;
  slug?: string;
  isActive?: boolean;
  isPublic?: boolean;
  createdAt?: string | Date | null;
  updatedAt?: string | Date | null;
  thankYouMessage?: string;
  redirectUrl?: string;
  notifyEmail?: string;
  shareUrl?: string;
  fields?: IntakePdfField[];
}

const dateFormatter = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: '2-digit',
  year: 'numeric'
});

const formatDate = (value?: string | Date | null) => {
  if (!value) return '';
  const parsed = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(parsed.getTime())) return '';
  return dateFormatter.format(parsed);
};

const slugifyFilename = (value?: string) => {
  const normalized = (value || 'intake-form')
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');

  return normalized || 'intake-form';
};

const humanizeFieldType = (type?: string) => {
  switch ((type || '').toLowerCase()) {
    case 'text':
      return 'Text input';
    case 'email':
      return 'Email';
    case 'phone':
      return 'Phone';
    case 'textarea':
      return 'Text area';
    case 'select':
      return 'Dropdown';
    case 'checkbox':
      return 'Checkbox';
    case 'radio':
      return 'Radio';
    case 'date':
      return 'Date';
    case 'file':
      return 'File upload (coming soon)';
    default:
      return type || 'Field';
  }
};

const buildConditionalSummary = (field: IntakePdfField, fields: IntakePdfField[]) => {
  const rule = parseConditionalLogic(field.conditionalLogic);
  if (!rule) return '';

  const sourceField = fields.find(candidate =>
    candidate.name?.toLowerCase() === rule.sourceField.toLowerCase()
  );

  const sourceLabel = sourceField?.label || rule.sourceField;
  const rawValue = typeof rule.value === 'boolean'
    ? (rule.value ? 'true' : 'false')
    : String(rule.value ?? '');

  return `Show when ${sourceLabel} is ${rawValue}.`;
};

const pushBlock = (lines: string[], label: string, value?: string) => {
  const normalized = value?.trim();
  if (!normalized) return;
  lines.push(`${label}: ${normalized}`);
};

export const downloadIntakeFormPdf = async (form: IntakeFormPdfData) => {
  const { jsPDF } = await import('jspdf');

  const doc = new jsPDF({ unit: 'pt', format: 'letter' });
  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const margin = 40;
  const contentWidth = pageWidth - (margin * 2);
  const columnGap = 20;
  const columnWidth = (contentWidth - columnGap) / 2;
  const fields = Array.isArray(form.fields)
    ? [...form.fields].sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
    : [];

  let y = margin;

  const ensureSpace = (height: number) => {
    if (y + height <= pageHeight - margin) return;
    doc.addPage();
    y = margin;
  };

  const writeMuted = (text: string, x: number, ypos: number, align: 'left' | 'right' = 'left') => {
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(100, 116, 139);
    doc.text(text, x, ypos, { align });
  };

  const writeStrong = (
    text: string,
    x: number,
    ypos: number,
    options?: { size?: number; align?: 'left' | 'right' }
  ) => {
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(options?.size ?? 12);
    doc.setTextColor(15, 23, 42);
    doc.text(text, x, ypos, { align: options?.align ?? 'left' });
  };

  const writeWrappedText = (text: string, x: number, ypos: number, width: number, tone: 'muted' | 'body' = 'body') => {
    const lines = doc.splitTextToSize(text, width);
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(tone === 'muted' ? 100 : 51, tone === 'muted' ? 116 : 65, tone === 'muted' ? 139 : 85);
    doc.text(lines, x, ypos);
    return lines.length;
  };

  const drawMetaCard = (x: number, title: string, value: string) => {
    const titleLines = doc.splitTextToSize(title, columnWidth - 28);
    const valueLines = doc.splitTextToSize(value, columnWidth - 28);
    const cardHeight = 18 + (titleLines.length * 10) + (valueLines.length * 12) + 18;

    doc.setDrawColor(226, 232, 240);
    doc.setFillColor(248, 250, 252);
    doc.roundedRect(x, y, columnWidth, cardHeight, 16, 16, 'FD');

    doc.setFont('helvetica', 'bold');
    doc.setFontSize(9);
    doc.setTextColor(100, 116, 139);
    doc.text(titleLines, x + 14, y + 18);

    doc.setFont('helvetica', 'normal');
    doc.setFontSize(11);
    doc.setTextColor(15, 23, 42);
    doc.text(valueLines, x + 14, y + 34 + ((titleLines.length - 1) * 10));

    return cardHeight;
  };

  writeStrong(form.name?.trim() || 'Untitled Intake Form', margin, y, { size: 22 });
  y += 20;
  writeMuted('Intake Form Blueprint', margin, y);
  if (form.slug) {
    writeMuted(`Slug: ${form.slug}`, pageWidth - margin, y, 'right');
  }
  y += 22;

  if (form.description?.trim()) {
    const descriptionLines = writeWrappedText(form.description.trim(), margin, y, contentWidth, 'body');
    y += (descriptionLines * 12) + 14;
  }

  const metaRows: Array<[string, string]> = [
    ['Practice Area', form.practiceArea?.trim() || 'Not set'],
    ['Visibility', form.isPublic ? 'Public' : 'Private'],
    ['Status', form.isActive ? 'Active' : 'Draft'],
    ['Fields', `${fields.length} total / ${fields.filter(field => field.required).length} required`]
  ];

  if (form.shareUrl?.trim()) {
    metaRows.push(['Share Link', form.shareUrl.trim()]);
  }
  if (formatDate(form.createdAt)) {
    metaRows.push(['Created', formatDate(form.createdAt)]);
  }
  if (formatDate(form.updatedAt)) {
    metaRows.push(['Updated', formatDate(form.updatedAt)]);
  }

  for (let index = 0; index < metaRows.length; index += 2) {
    const left = metaRows[index];
    const right = metaRows[index + 1];
    const leftHeight = drawMetaCard(margin, left[0], left[1]);
    const rightHeight = right
      ? drawMetaCard(margin + columnWidth + columnGap, right[0], right[1])
      : leftHeight;

    y += Math.max(leftHeight, rightHeight) + 14;
  }

  ensureSpace(40);
  doc.setDrawColor(226, 232, 240);
  doc.line(margin, y, pageWidth - margin, y);
  y += 22;
  writeStrong('Field Structure', margin, y, { size: 14 });
  y += 18;

  if (fields.length === 0) {
    writeMuted('No fields have been configured for this form yet.', margin, y);
    y += 22;
  } else {
    fields.forEach((field, index) => {
      const blockLines = [
        `${humanizeFieldType(field.type)} | ${field.required ? 'Required' : 'Optional'} | ${field.name || 'unnamed_field'}`
      ];

      pushBlock(blockLines, 'Placeholder', field.placeholder);
      pushBlock(blockLines, 'Help text', field.helpText);
      pushBlock(blockLines, 'Default value', field.defaultValue);

      const options = parseFieldOptions(field.options);
      if (options.length > 0) {
        pushBlock(blockLines, 'Options', options.join(', '));
      }

      if (field.validationPattern?.trim()) {
        const validationLabel = field.validationMessage?.trim()
          ? `${field.validationPattern.trim()} (${field.validationMessage.trim()})`
          : field.validationPattern.trim();
        pushBlock(blockLines, 'Validation', validationLabel);
      }

      const conditionalSummary = buildConditionalSummary(field, fields);
      pushBlock(blockLines, 'Conditional visibility', conditionalSummary);

      const wrappedLines = blockLines.flatMap(line => doc.splitTextToSize(line, contentWidth - 32));
      const blockHeight = 24 + (wrappedLines.length * 12) + 20;

      ensureSpace(blockHeight + 12);

      doc.setDrawColor(226, 232, 240);
      doc.setFillColor(255, 255, 255);
      doc.roundedRect(margin, y, contentWidth, blockHeight, 16, 16, 'FD');

      writeStrong(`${index + 1}. ${field.label?.trim() || 'Untitled Field'}`, margin + 16, y + 22, { size: 12 });
      doc.setFont('helvetica', 'normal');
      doc.setFontSize(10);
      doc.setTextColor(71, 85, 105);
      doc.text(wrappedLines, margin + 16, y + 40);

      y += blockHeight + 12;
    });
  }

  const followUpRows = [
    ['Thank You Message', form.thankYouMessage?.trim() || 'Default thank-you copy'],
    ['Redirect URL', form.redirectUrl?.trim() || 'Not set'],
    ['Notify Email', form.notifyEmail?.trim() || 'Not set']
  ];

  ensureSpace(110);
  doc.setDrawColor(226, 232, 240);
  doc.line(margin, y, pageWidth - margin, y);
  y += 22;
  writeStrong('Submission Follow-up', margin, y, { size: 14 });
  y += 18;

  followUpRows.forEach(([label, value]) => {
    const lines = doc.splitTextToSize(value, contentWidth - 30);
    const rowHeight = 18 + (lines.length * 12) + 16;

    ensureSpace(rowHeight + 8);
    doc.setDrawColor(226, 232, 240);
    doc.setFillColor(248, 250, 252);
    doc.roundedRect(margin, y, contentWidth, rowHeight, 16, 16, 'FD');
    writeStrong(label, margin + 14, y + 20, { size: 11 });
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(71, 85, 105);
    doc.text(lines, margin + 14, y + 38);
    y += rowHeight + 10;
  });

  const footerY = pageHeight - margin + 4;
  doc.setDrawColor(226, 232, 240);
  doc.line(margin, footerY - 18, pageWidth - margin, footerY - 18);
  writeMuted('Generated from JurisFlow intake builder. This PDF reflects the configured form structure at export time.', margin, footerY);

  doc.save(`${slugifyFilename(form.slug || form.name)}.pdf`);
};
