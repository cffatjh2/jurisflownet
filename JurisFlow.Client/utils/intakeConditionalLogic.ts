export interface ConditionalLogicField {
    id?: string;
    name: string;
    label?: string;
    type: string;
    options?: string;
    defaultValue?: string;
    conditionalLogic?: string;
    order?: number;
}

export interface IntakeConditionalLogicRule {
    action: 'show';
    operator: 'equals';
    sourceField: string;
    value: string;
}

type ConditionalOption = {
    label: string;
    value: string;
};

const CONDITIONAL_SOURCE_TYPES = new Set(['select', 'radio', 'checkbox']);

export const parseFieldOptions = (rawOptions?: string) =>
    (rawOptions || '')
        .split('\n')
        .map(option => option.trim())
        .filter(Boolean);

export const supportsConditionalSourceField = (type?: string) =>
    CONDITIONAL_SOURCE_TYPES.has((type || '').trim().toLowerCase());

export const getCheckboxDefaultValue = (value?: string) => (value || '').trim().toLowerCase() === 'true';

export const getFieldDefaultValue = (field: ConditionalLogicField): string | boolean => {
    if (!field.defaultValue) {
        return field.type === 'checkbox' ? false : '';
    }

    const normalizedDefaultValue = field.defaultValue.trim();

    if (field.type === 'checkbox') {
        return getCheckboxDefaultValue(normalizedDefaultValue);
    }

    if (field.type === 'select' || field.type === 'radio') {
        const options = parseFieldOptions(field.options);
        return options.includes(normalizedDefaultValue) ? normalizedDefaultValue : '';
    }

    return normalizedDefaultValue;
};

export const getConditionalRuleValueOptions = (field: ConditionalLogicField): ConditionalOption[] => {
    const normalizedType = (field.type || '').trim().toLowerCase();

    if (normalizedType === 'checkbox') {
        return [
            { label: 'Checked', value: 'true' },
            { label: 'Unchecked', value: 'false' }
        ];
    }

    if (normalizedType === 'select' || normalizedType === 'radio') {
        return parseFieldOptions(field.options).map(option => ({
            label: option,
            value: option
        }));
    }

    return [];
};

export const parseConditionalLogic = (rawConditionalLogic?: string): IntakeConditionalLogicRule | null => {
    const trimmed = rawConditionalLogic?.trim();
    if (!trimmed) {
        return null;
    }

    try {
        const parsed = JSON.parse(trimmed) as Partial<IntakeConditionalLogicRule>;
        const sourceField = parsed.sourceField?.trim();
        const action = parsed.action || 'show';
        const operator = parsed.operator || 'equals';
        const value = typeof parsed.value === 'string'
            ? parsed.value.trim()
            : typeof parsed.value === 'boolean'
                ? String(parsed.value)
                : '';

        if (!sourceField || action !== 'show' || operator !== 'equals') {
            return null;
        }

        return {
            action,
            operator,
            sourceField,
            value
        };
    } catch {
        return null;
    }
};

export const serializeConditionalLogic = (rule: IntakeConditionalLogicRule | null) =>
    rule ? JSON.stringify(rule) : '';

export const normalizeConditionalRuleValue = (sourceField: ConditionalLogicField, rawValue: unknown) => {
    if (sourceField.type === 'checkbox') {
        if (typeof rawValue === 'boolean') {
            return rawValue ? 'true' : 'false';
        }

        const normalized = String(rawValue ?? '').trim().toLowerCase();
        return normalized === 'true' ? 'true' : 'false';
    }

    return String(rawValue ?? '').trim();
};

export const getComparableConditionalValue = (field: ConditionalLogicField, rawValue: unknown) => {
    if (field.type === 'checkbox') {
        if (typeof rawValue === 'boolean') {
            return rawValue ? 'true' : 'false';
        }

        if (typeof rawValue === 'string' && rawValue.trim()) {
            return rawValue.trim().toLowerCase() === 'true' ? 'true' : 'false';
        }

        return getCheckboxDefaultValue(field.defaultValue) ? 'true' : 'false';
    }

    if (typeof rawValue === 'string' && rawValue.trim()) {
        return rawValue.trim();
    }

    return typeof field.defaultValue === 'string' ? field.defaultValue.trim() : '';
};

const getOrderedFields = <T extends ConditionalLogicField>(fields: T[]) =>
    [...fields].sort((left, right) => (left.order ?? 0) - (right.order ?? 0));

export const getVisibleFieldNames = <T extends ConditionalLogicField>(
    fields: T[],
    values: Record<string, unknown>
) => {
    const orderedFields = getOrderedFields(fields);
    const fieldLookup = new Map(
        orderedFields.map(field => [field.name.toLowerCase(), field])
    );
    const visibleFieldNames = new Set<string>();

    for (const field of orderedFields) {
        const rule = parseConditionalLogic(field.conditionalLogic);
        if (!rule) {
            visibleFieldNames.add(field.name);
            continue;
        }

        const sourceField = fieldLookup.get(rule.sourceField.toLowerCase());
        if (!sourceField || !supportsConditionalSourceField(sourceField.type)) {
            visibleFieldNames.add(field.name);
            continue;
        }

        if (!visibleFieldNames.has(sourceField.name)) {
            continue;
        }

        const actualValue = getComparableConditionalValue(sourceField, values[sourceField.name]);
        const expectedValue = normalizeConditionalRuleValue(sourceField, rule.value);
        if (actualValue === expectedValue) {
            visibleFieldNames.add(field.name);
        }
    }

    return visibleFieldNames;
};

export const getVisibleFields = <T extends ConditionalLogicField>(
    fields: T[],
    values: Record<string, unknown>
) => {
    const visibleFieldNames = getVisibleFieldNames(fields, values);
    return getOrderedFields(fields).filter(field => visibleFieldNames.has(field.name));
};

export const filterSubmissionValuesByVisibility = <T extends ConditionalLogicField>(
    fields: T[],
    values: Record<string, unknown>
) => {
    const visibleFieldNames = getVisibleFieldNames(fields, values);
    const filteredValues: Record<string, unknown> = {};

    for (const field of getOrderedFields(fields)) {
        if (visibleFieldNames.has(field.name) && Object.prototype.hasOwnProperty.call(values, field.name)) {
            filteredValues[field.name] = values[field.name];
        }
    }

    return filteredValues;
};

export const sanitizeConditionalLogicField = <T extends ConditionalLogicField>(
    field: T,
    availableSourceFields: ConditionalLogicField[]
) => {
    const rule = parseConditionalLogic(field.conditionalLogic);
    if (!rule) {
        return {
            ...field,
            conditionalLogic: ''
        };
    }

    const sourceField = availableSourceFields.find(candidate =>
        candidate.name.toLowerCase() === rule.sourceField.toLowerCase()
    );
    if (!sourceField || !supportsConditionalSourceField(sourceField.type)) {
        return {
            ...field,
            conditionalLogic: ''
        };
    }

    const allowedValues = getConditionalRuleValueOptions(sourceField).map(option => option.value);
    const normalizedRuleValue = normalizeConditionalRuleValue(sourceField, rule.value);
    if (allowedValues.length === 0 || !allowedValues.includes(normalizedRuleValue)) {
        return {
            ...field,
            conditionalLogic: ''
        };
    }

    return {
        ...field,
        conditionalLogic: serializeConditionalLogic({
            action: 'show',
            operator: 'equals',
            sourceField: sourceField.name,
            value: normalizedRuleValue
        })
    };
};

export const normalizeConditionalLogicFields = <T extends ConditionalLogicField>(fields: T[]) => {
    const orderedFields = getOrderedFields(fields);
    return orderedFields.map((field, index) =>
        sanitizeConditionalLogicField(field, orderedFields.slice(0, index).filter(candidate =>
            supportsConditionalSourceField(candidate.type) && getConditionalRuleValueOptions(candidate).length > 0
        ))
    );
};
