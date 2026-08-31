import {
  useEffect,
  useMemo,
  useState,
} from 'react'

import { TicketForm } from '../components/TicketForm'
import { TicketQueue } from '../components/TicketQueue'
import { TicketDetails } from '../components/TicketDetails'
import { SummaryCard } from '../components/SummaryCard'
import { api, ApiError } from '../services/api'

import type {
  Notice,
  Role,
  Ticket,
  TicketSort,
} from '../types'

export function TicketsPage({
  role,
  setNotice,
}: {
  role: Role
  setNotice: (notice: Notice) => void
}) {
  const [tickets, setTickets] =
    useState<Ticket[]>([])

  const [selectedId, setSelectedId] =
    useState<number | null>(null)

  /*
    USER STORY:
    Newest / Oldest ticket sorting.

    Technicians can also use the team's
    Priority + Age sorting option.
  */
  const [sort, setSort] =
    useState<TicketSort>(
      role === 'IT Technician'
        ? 'priority-age'
        : 'newest',
    )

  const selected = useMemo(
    () =>
      tickets.find(
        ticket => ticket.id === selectedId,
      ) ?? null,
    [tickets, selectedId],
  )

  useEffect(() => {
    setSort(
      role === 'IT Technician'
        ? 'priority-age'
        : 'newest',
    )
  }, [role])

  /*
    Reload tickets when the sort option changes.
  */
  useEffect(() => {
    void load()
  }, [sort])

  async function load(preferredId?: number) {
    try {
      const data = await api.getTickets(sort)

      setTickets(data)

      if (
        preferredId &&
        data.some(
          ticket => ticket.id === preferredId,
        )
      ) {
        setSelectedId(preferredId)
      } else if (
        !data.some(
          ticket => ticket.id === selectedId,
        )
      ) {
        setSelectedId(
          data[0]?.id ?? null,
        )
      }
    } catch (error) {
      setNotice({
        type: 'error',
        text:
          error instanceof ApiError
            ? error.message
            : 'Could not load tickets.',
      })
    }
  }

  async function changed(ticket: Ticket) {
    await load(ticket.id)
  }

  return (
    <>
      <section className="page-heading">
        <div>
          <p className="eyebrow">
            TICKET MANAGEMENT
          </p>

          <h2>Airport IT support</h2>

          <p>
            Structured reporting, operational
            impact, status tracking and IT
            workflow.
          </p>
        </div>

        <div className="role-chip">
          {role}
        </div>
      </section>

      <section className="summary-grid">
        <SummaryCard
          label="Total tickets"
          value={tickets.length}
          note="Loaded from SQLite"
        />

        <SummaryCard
          label="Unresolved"
          value={
            tickets.filter(
              ticket =>
                ticket.status !== 'Closed',
            ).length
          }
          note="Every status except Closed"
        />

        <SummaryCard
          label="Passenger impact"
          value={
            tickets.filter(
              ticket =>
                ticket.passengerImpact,
            ).length
          }
          note="Visible in the queue"
        />

        <SummaryCard
          label="SLA overdue"
          value={
            tickets.filter(
              ticket => ticket.slaOverdue,
            ).length
          }
          note="Priority-based rule"
          danger
        />
      </section>

      {role === 'Airport Staff' && (
        <TicketForm
          onCreated={changed}
          setNotice={setNotice}
        />
      )}

      <section className="ticket-layout">
        <TicketQueue
          tickets={tickets}
          selectedId={selectedId}
          onSelect={setSelectedId}
          sort={sort}
          onSort={setSort}
          technicianView={
            role !== 'Airport Staff'
          }
        />

        <TicketDetails
          ticket={selected}
          role={role}
          onChanged={changed}
          setNotice={setNotice}
        />
      </section>
    </>
  )
}