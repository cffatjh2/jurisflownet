import React, { useEffect, useState } from 'react';
import { FirmEntity, Office } from '../../types';
import { api } from '../../services/api';

interface EntityOfficeFilterProps {
    entityId: string;
    officeId: string;
    onEntityChange: (id: string) => void;
    onOfficeChange: (id: string) => void;
    allowAll?: boolean;
    autoSelectDefault?: boolean;
    className?: string;
}

const EntityOfficeFilter: React.FC<EntityOfficeFilterProps> = ({
    entityId,
    officeId,
    onEntityChange,
    onOfficeChange,
    allowAll = false,
    autoSelectDefault,
    className
}) => {
    const [entities, setEntities] = useState<FirmEntity[]>([]);
    const [offices, setOffices] = useState<Office[]>([]);

    const shouldAutoSelect = autoSelectDefault ?? !allowAll;

    useEffect(() => {
        const loadEntities = async () => {
            try {
                const data = await api.entities.list();
                setEntities(Array.isArray(data) ? data.filter((entity): entity is FirmEntity => !!entity && typeof entity.id === 'string') : []);
            } catch (error) {
                console.error('Failed to load entities', error);
                setEntities([]);
            }
        };
        loadEntities();
    }, []);

    useEffect(() => {
        if (!entityId) {
            setOffices([]);
            return;
        }
        const loadOffices = async () => {
            try {
                const data = await api.entities.offices.list(entityId);
                setOffices(Array.isArray(data) ? data.filter((office): office is Office => !!office && typeof office.id === 'string') : []);
            } catch (error) {
                console.error('Failed to load offices', error);
                setOffices([]);
            }
        };
        loadOffices();
    }, [entityId]);

    useEffect(() => {
        if (!shouldAutoSelect || entityId || entities.length === 0) return;
        const preferred = entities.find(e => e.isDefault) || entities[0];
        if (preferred) {
            onEntityChange(preferred.id);
        }
    }, [entities, entityId, onEntityChange, shouldAutoSelect]);

    useEffect(() => {
        if (!shouldAutoSelect || officeId || offices.length === 0) return;
        const preferred = offices.find(o => o.isDefault) || offices[0];
        if (preferred) {
            onOfficeChange(preferred.id);
        }
    }, [offices, officeId, onOfficeChange, shouldAutoSelect]);

    return (
        <div className={`flex flex-wrap gap-3 ${className || ''}`}>
            <div className="flex items-center gap-2 bg-white border border-gray-200 rounded-lg px-3 py-2 text-sm">
                <span className="text-gray-500">Entity</span>
                <select
                    value={entityId}
                    onChange={(e) => {
                        onEntityChange(e.target.value);
                        onOfficeChange('');
                    }}
                    className="bg-transparent outline-none text-sm text-gray-700"
                >
                    {allowAll && <option value="">All</option>}
                    {!allowAll && <option value="">Select</option>}
                    {entities.map(entity => (
                        <option key={entity.id} value={entity.id}>{entity.name}</option>
                    ))}
                </select>
            </div>
            <div className="flex items-center gap-2 bg-white border border-gray-200 rounded-lg px-3 py-2 text-sm">
                <span className="text-gray-500">Office</span>
                <select
                    value={officeId}
                    onChange={(e) => onOfficeChange(e.target.value)}
                    className="bg-transparent outline-none text-sm text-gray-700"
                    disabled={!entityId}
                >
                    {allowAll && <option value="">All</option>}
                    {!allowAll && <option value="">Select</option>}
                    {offices.map(office => (
                        <option key={office.id} value={office.id}>{office.name}</option>
                    ))}
                </select>
            </div>
        </div>
    );
};

export default EntityOfficeFilter;
